using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ARMClient.Authentication;
using ARMClient.Authentication.AADAuthentication;
using ARMClient.Authentication.Contracts;
using Microsoft.CSharp.RuntimeBinder;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Net.Sockets;

namespace ARMClient.Library
{
    public class AzureClient : IAzureClient
    {
        private TokenCacheInfo _tokenCacheInfo;
        private readonly IAuthHelper _authHelper;
        private readonly AzureEnvironments _azureEnvironment;
        private LoginType _loginType;
        private string _tenantId;
        private string _appId;
        private string _appKey;
        private string _userName;
        private string _password;
        private int _retryCount;
        private Random rand = new Random();

        public AzureClient(int retryCount = 0, AzureEnvironments azureEnvironment = AzureEnvironments.Prod)
        {
            this._authHelper = new AuthHelper();
            this._azureEnvironment = azureEnvironment;
            this._retryCount = retryCount;
            this._loginType = LoginType.Interactive;
        }

        public void ConfigureSpnLogin(string tenantId, string appId, string appKey)
        {
            this._loginType = LoginType.Spn;
            this._tenantId = tenantId;
            this._appId = appId;
            this._appKey = appKey;
        }

        public void ConfigureUpnLogin(string userName, string password)
        {
            this._loginType = LoginType.Upn;
            this._userName = userName;
            this._password = password;
        }

        public Task<HttpResponseMessage> HttpInvoke(HttpMethod method, Uri uri, object objectPayload = null)
        {
            return HttpInvoke(method.Method, uri, objectPayload);
        }

        public async Task<HttpResponseMessage> HttpInvoke(string method, Uri uri, object objectPayload = null)
        {
            var socketTrials = 10;
            var retries = this._retryCount;
            while (true)
            {
                try
                {
                    var response = await HttpInvoke(uri, method, objectPayload);

                    if (!response.IsSuccessStatusCode && this._retryCount > 0)
                    {
                        while (retries > 0)
                        {
                            response = await HttpInvoke(uri, method, objectPayload);
                            if (response.IsSuccessStatusCode)
                            {
                                return response;
                            }
                            else
                            {
                                retries--;
                            }
                        }
                    }
                    return response;
                }
                catch (SocketException)
                {
                    if (socketTrials <= 0) throw;
                    socketTrials--;
                }
                catch (Exception)
                {
                    if (retries <= 0) throw;
                    retries--;
                }
                await Task.Delay(rand.Next(1000, 10000));
            }
        }

        private async Task<HttpResponseMessage> HttpInvoke(Uri uri, string verb, object objectPayload)
        {
            var payload = JsonConvert.SerializeObject(objectPayload);
            using (var client = new HttpClient(new HttpClientHandler()))
            {
                client.DefaultRequestHeaders.Add("Authorization", await GetAuthorizationHeader(uri.AbsoluteUri));
                client.DefaultRequestHeaders.Add("User-Agent", Constants.UserAgent.Value);
                client.DefaultRequestHeaders.Add("Accept", Constants.JsonContentType);
                client.DefaultRequestHeaders.Add("x-ms-request-id", Guid.NewGuid().ToString());

                HttpResponseMessage response = null;
                if (String.Equals(verb, "get", StringComparison.OrdinalIgnoreCase))
                {
                    response = await client.GetAsync(uri).ConfigureAwait(false);
                }
                else if (String.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase))
                {
                    response = await client.DeleteAsync(uri).ConfigureAwait(false);
                }
                else if (String.Equals(verb, "post", StringComparison.OrdinalIgnoreCase))
                {
                    response = await client.PostAsync(uri, new StringContent(payload ?? String.Empty, Encoding.UTF8, Constants.JsonContentType)).ConfigureAwait(false);
                }
                else if (String.Equals(verb, "put", StringComparison.OrdinalIgnoreCase))
                {
                    response = await client.PutAsync(uri, new StringContent(payload ?? String.Empty, Encoding.UTF8, Constants.JsonContentType)).ConfigureAwait(false);
                }
                else if (String.Equals(verb, "patch", StringComparison.OrdinalIgnoreCase))
                {
                    using (var message = new HttpRequestMessage(new HttpMethod("PATCH"), uri))
                    {
                        message.Content = new StringContent(payload ?? String.Empty, Encoding.UTF8, Constants.JsonContentType);
                        response = await client.SendAsync(message).ConfigureAwait(false);
                    }
                }
                else
                {
                    throw new InvalidOperationException(String.Format("Invalid http verb '{0}'!", verb));
                }

                return response;
            }
        }

        private async Task<string> GetAuthorizationHeader(string url)
        {
            var match = Regex.Match(url, ".*\\/subscriptions\\/(.*?)\\/", RegexOptions.IgnoreCase);
            var subscriptionId = match.Success ? match.Groups[1].ToString() : null;

            if (this._tokenCacheInfo == null || this._tokenCacheInfo.ExpiresOn <= DateTimeOffset.UtcNow)
            {
                switch (this._loginType)
                {
                    case LoginType.Interactive:
                        await this._authHelper.AcquireTokens().ConfigureAwait(false);
                        break;
                    case LoginType.Spn:
                        await this._authHelper.GetTokenBySpn(this._tenantId, this._appId, this._appKey).ConfigureAwait(false);
                        break;
                    case LoginType.Upn:
                        await this._authHelper.GetTokenByUpn(this._userName, this._password).ConfigureAwait(false);
                        break;
                }
                this._tokenCacheInfo = await this._authHelper.GetToken(subscriptionId, Constants.CSMResource).ConfigureAwait(false);
            }
            return this._tokenCacheInfo.CreateAuthorizationHeader();
        }
    }
}