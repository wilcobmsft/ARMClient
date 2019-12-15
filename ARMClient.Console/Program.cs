﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using ARMClient.Authentication;
using ARMClient.Authentication.AADAuthentication;
using ARMClient.Authentication.Contracts;
using ARMClient.Authentication.Utilities;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Net.Security;

namespace ARMClient
{
    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;
            System.Net.ServicePointManager.ServerCertificateValidationCallback = (object context, X509Certificate cert, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
            {
                return true;
            };

            Utils.SetTraceListener(new ConsoleTraceListener());
            try
            {
                var persistentAuthHelper = new PersistentAuthHelper();
                if (args.Length > 0)
                {
                    var _parameters = new CommandLineParameters(args);
                    var verb = _parameters.Get(0, "verb");
                    if (String.Equals(verb, "login", StringComparison.OrdinalIgnoreCase))
                    {
                        var env = _parameters.Get(1, requires: false);
                        _parameters.ThrowIfUnknown();

                        persistentAuthHelper.AzureEnvironments = env == null ? Utils.GetDefaultEnv() :
                            (AzureEnvironments)Enum.Parse(typeof(AzureEnvironments), args[1], ignoreCase: true);
                        persistentAuthHelper.AcquireTokens().Wait();
                        return 0;
                    }
                    else if (String.Equals(verb, "azlogin", StringComparison.OrdinalIgnoreCase))
                    {
                        _parameters.ThrowIfUnknown();

                        persistentAuthHelper.AzureEnvironments = AzureEnvironments.Prod;
                        persistentAuthHelper.AzLogin().Wait();
                        return 0;
                    }
                    else if (String.Equals(verb, "listcache", StringComparison.OrdinalIgnoreCase))
                    {
                        _parameters.ThrowIfUnknown();
                        EnsureTokenCache(persistentAuthHelper);

                        foreach (var line in persistentAuthHelper.DumpTokenCache())
                        {
                            Console.WriteLine(line);
                        }
                        return 0;
                    }
                    else if (String.Equals(verb, "clearcache", StringComparison.OrdinalIgnoreCase))
                    {
                        _parameters.ThrowIfUnknown();
                        persistentAuthHelper.ClearTokenCache();
                        return 0;
                    }
                    else if (String.Equals(verb, "token", StringComparison.OrdinalIgnoreCase))
                    {
                        var tenantId = _parameters.Get(1, requires: false);
                        _parameters.ThrowIfUnknown();

                        if (tenantId == null)
                        {
                            var accessToken = Utils.GetDefaultToken();
                            if (!String.IsNullOrEmpty(accessToken))
                            {
                                DumpClaims(accessToken);
                                Console.WriteLine();
                                return 0;
                            }
                        }

                        if (tenantId != null && tenantId.StartsWith("ey"))
                        {
                            DumpClaims(tenantId);
                            return 0;
                        }

                        EnsureTokenCache(persistentAuthHelper);

                        persistentAuthHelper.AzureEnvironments = Utils.GetDefaultEnv();

                        TokenCacheInfo cacheInfo;
                        Uri resourceUri = null;
                        if (Uri.TryCreate(tenantId, UriKind.Absolute, out resourceUri))
                        {
                            // https://vault.azure.net (no trailing /)
                            // https://graph.windows.net (no trailing /)
                            // https://management.core.windows.net/
                            cacheInfo = persistentAuthHelper.GetTokenByResource(tenantId).Result;
                        }
                        else
                        {
                            cacheInfo = persistentAuthHelper.GetToken(tenantId, null).Result;
                        }

                        var bearer = cacheInfo.CreateAuthorizationHeader();
                        Clipboard.SetText(cacheInfo.AccessToken);
                        DumpClaims(cacheInfo.AccessToken);
                        Console.WriteLine();
                        Console.WriteLine("Token copied to clipboard successfully.");
                        return 0;
                    }
                    else if (String.Equals(verb, "spn", StringComparison.OrdinalIgnoreCase))
                    {
                        var tenantId = _parameters.Get(1, keyName: "tenant");
                        var appId = _parameters.Get(2, keyName: "appId");
                        var resource = Environment.GetEnvironmentVariable("ARMCLIENT_RESOURCE");
                        EnsureGuidFormat(appId);

                        X509Certificate2 certificate = null;
                        var appKey = _parameters.Get(3, keyName: "appKey", requires: false);
                        if (appKey == null)
                        {
                            var certSubjectName = Environment.GetEnvironmentVariable("ARMCLIENT_CERT");
                            if (!string.IsNullOrWhiteSpace(certSubjectName))
                            {
                                certificate = FindCertificate(certSubjectName);
                            }
                            else
                            {
                                appKey = PromptForPassword("appKey");
                            }
                        }
                        else
                        {
                            if (File.Exists(appKey))
                            {
                                var password = _parameters.Get(4, keyName: "password", requires: false);
                                if (password == null)
                                {
                                    password = appKey + ".txt";
                                    if (!File.Exists(password))
                                    {
                                        password = PromptForPassword("password");
                                    }
                                }

                                if (File.Exists(password))
                                {
                                    certificate = new X509Certificate2(appKey, File.ReadAllText(password));
                                }
                                else
                                {
                                    certificate = new X509Certificate2(appKey, password);
                                }
                            }
                        }

                        if (certificate == null)
                        {
                            appKey = Utils.EnsureBase64Key(appKey);
                        }

                        _parameters.ThrowIfUnknown();

                        persistentAuthHelper.AzureEnvironments = Utils.GetDefaultEnv();
                        var cacheInfo = certificate != null ?
                            persistentAuthHelper.GetTokenBySpn(tenantId, appId, certificate, resource).Result :
                            persistentAuthHelper.GetTokenBySpn(tenantId, appId, appKey, resource).Result;
                        return 0;
                    }
                    else if (String.Equals(verb, "upn", StringComparison.OrdinalIgnoreCase))
                    {
                        var username = _parameters.Get(1, keyName: "username");
                        var password = _parameters.Get(2, keyName: "password", requires: false);
                        if (password == null)
                        {
                            password = PromptForPassword("password");
                        }
                        _parameters.ThrowIfUnknown();

                        persistentAuthHelper.AzureEnvironments = Utils.GetDefaultEnv();
                        var cacheInfo = persistentAuthHelper.GetTokenByUpn(username, password).Result;
                        return 0;
                    }
                    else if (String.Equals(verb, "get", StringComparison.OrdinalIgnoreCase)
                        || String.Equals(verb, "delete", StringComparison.OrdinalIgnoreCase)
                        || String.Equals(verb, "put", StringComparison.OrdinalIgnoreCase)
                        || String.Equals(verb, "post", StringComparison.OrdinalIgnoreCase)
                        || String.Equals(verb, "patch", StringComparison.OrdinalIgnoreCase))
                    {
                        var path = _parameters.Get(1, keyName: "url");
                        var verbose = _parameters.Get("-verbose", requires: false) != null || Utils.GetDefaultVerbose();
                        if (!verbose)
                        {
                            Trace.Listeners.Clear();
                        }

                        var content = ParseHttpContent(verb, _parameters);
                        var headers = _parameters.GetValue<Dictionary<string, List<string>>>("-h", requires: false);
                        var resource = Environment.GetEnvironmentVariable("ARMCLIENT_RESOURCE");
                        _parameters.ThrowIfUnknown();

                        var uri = Utils.EnsureAbsoluteUri(path, persistentAuthHelper);
                        var accessToken = Utils.GetDefaultToken();
                        if (!String.IsNullOrEmpty(accessToken))
                        {
                            return HttpInvoke(uri, new TokenCacheInfo { AccessToken = accessToken }, verb, verbose, content, headers).Result;
                        }

                        var env = string.IsNullOrWhiteSpace(resource)
                            ? GetAzureEnvironments(uri, persistentAuthHelper)
                            : AzureEnvironments.Prod;
                        if (!persistentAuthHelper.IsCacheValid() || persistentAuthHelper.AzureEnvironments != env)
                        {
                            persistentAuthHelper.AzureEnvironments = env;
                            persistentAuthHelper.AcquireTokens(resource).Wait();
                        }

                        resource = resource ?? GetResource(uri, env);
                        var subscriptionId = GetTenantOrSubscription(uri);
                        TokenCacheInfo cacheInfo = persistentAuthHelper.GetToken(subscriptionId, resource).Result;
                        return HttpInvoke(uri, cacheInfo, verb, verbose, content, headers).Result;
                    }
                    else
                    {
                        throw new CommandLineException(String.Format("Parameter '{0}' is invalid!", verb));
                    }
                }

                PrintUsage();
                return 1;
            }
            catch (Exception ex)
            {
                DumpException(ex);
                return -1;
            }
        }

        static string PromptForPassword(string title)
        {
            string pass = String.Empty;
            Console.Write("Enter {0}: ", title);
            ConsoleKeyInfo key;

            while (true)
            {
                key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return pass;
                }
                else if (key.Key == ConsoleKey.Escape)
                {
                    while (pass.Length > 0)
                    {
                        pass = pass.Remove(pass.Length - 1);
                        Console.Write("\b \b");
                    }
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    if (pass.Length > 0)
                    {
                        pass = pass.Substring(0, pass.Length - 1);
                        Console.Write("\b \b");
                    }
                }
                else
                {
                    pass += key.KeyChar;
                    Console.Write("*");
                }
            }
        }

        static void EnsureGuidFormat(string parameter)
        {
            Guid result;
            if (!Guid.TryParse(parameter, out result))
            {
                throw new CommandLineException(String.Format("Parameter '{0}' is not a valid guid!", parameter));
            }
        }

        static void EnsureTokenCache(PersistentAuthHelper persistentAuthHelper)
        {
            if (!persistentAuthHelper.IsCacheValid())
            {
                throw new CommandLineException("There is no login token.  Please login to acquire token.");
            }
        }

        static void DumpClaims(string accessToken)
        {
            PrintColoredJson(Utils.ParseClaims(accessToken));
            Console.WriteLine();
        }

        static void DumpException(Exception ex)
        {
            if (Utils.GetDefaultVerbose())
            {
                Console.WriteLine(ex);
            }
            else
            {
                if (ex.InnerException != null)
                {
                    DumpException(ex.InnerException);
                }

                // Aggregate exceptions themselves don't have interesting messages
                if (!(ex is AggregateException))
                {
                    Console.WriteLine(ex.Message);
                }
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine(@"ARMClient version {0}", typeof(Program).Assembly.GetName().Version);
            Console.WriteLine("A simple tool to invoke the Azure Resource Manager API");
            Console.WriteLine("Source code is available on https://github.com/projectkudu/ARMClient.");

            Console.WriteLine();
            Console.WriteLine("Login and get tokens");
            Console.WriteLine("    ARMClient.exe login [environment name]");

            Console.WriteLine();
            Console.WriteLine("Login with Azure CLI 2.0 (az)");
            Console.WriteLine("    ARMClient.exe azlogin");

            Console.WriteLine();
            Console.WriteLine("Call ARM api");
            Console.WriteLine("    ARMClient.exe [get|post|put|patch|delete] [url] (<@file|content>) (-h \"header: value\") (-verbose)");
            Console.WriteLine("    Use '-h' multiple times to add more than one custom HTTP header.");

            Console.WriteLine();
            Console.WriteLine("Copy token to clipboard");
            Console.WriteLine("    ARMClient.exe token [tenant|subscription|resource]");

            Console.WriteLine();
            Console.WriteLine("Get token by ServicePrincipal");
            Console.WriteLine("    ARMClient.exe spn [tenant] [appId] (appKey)");
            Console.WriteLine("    ARMClient.exe spn [tenant] [appId] [certificate] (password)");

            Console.WriteLine();
            Console.WriteLine("Get token by Username/Password");
            Console.WriteLine("    ARMClient.exe upn [username] (password)");

            Console.WriteLine();
            Console.WriteLine("List token cache");
            Console.WriteLine("    ARMClient.exe listcache");

            Console.WriteLine();
            Console.WriteLine("Clear token cache");
            Console.WriteLine("    ARMClient.exe clearcache");
        }

        static HttpContent ParseHttpContent(string verb, CommandLineParameters parameters)
        {
            bool requiresData = String.Equals(verb, "put", StringComparison.OrdinalIgnoreCase)
                        || String.Equals(verb, "patch", StringComparison.OrdinalIgnoreCase);
            bool inputRedirected = Console.IsInputRedirected;

            if (requiresData || String.Equals(verb, "post", StringComparison.OrdinalIgnoreCase))
            {
                string data = parameters.Get("2", "content", requires: requiresData && !inputRedirected);
                if (data == null)
                {
                    if (inputRedirected)
                    {
                        return new StringContent(Console.In.ReadToEnd(), Encoding.UTF8, Constants.JsonContentType);
                    }

                    return new StringContent(String.Empty, Encoding.UTF8, Constants.JsonContentType);
                }

                if (data.StartsWith("@"))
                {
                    data = File.ReadAllText(data.Substring(1));
                }
                else if (File.Exists(data))
                {
                    data = File.ReadAllText(data);
                }

                return new StringContent(data, Encoding.UTF8, !string.IsNullOrEmpty(data) && data.StartsWith("<") ? Constants.XmlContentType : Constants.JsonContentType);
            }
            return null;
        }

        static async Task<int> HttpInvoke(Uri uri, TokenCacheInfo cacheInfo, string verb, bool verbose, HttpContent content, Dictionary<string, List<string>> headers)
        {
            var primaryHandler = new WebRequestHandler();
            if (Utils.IsCustom(uri))
            {
                var certSubjectName = Environment.GetEnvironmentVariable("ARMCLIENT_CERT");
                if (string.IsNullOrWhiteSpace(certSubjectName))
                {
                    throw new Exception("ARMCLIENT_CERT environment variable is required when invoking a custom url.");
                }
                
                primaryHandler.ClientCertificates.Add(FindCertificate(certSubjectName));
            }

            var logginerHandler = new HttpLoggingHandler(primaryHandler, verbose);
            return await Utils.HttpInvoke(uri, cacheInfo, verb, logginerHandler, content, headers);
        }

        private static X509Certificate2 FindCertificate(string certSubjectName)
        {
            using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
            {
                store.Open(OpenFlags.ReadOnly);
                var certs = store.Certificates.Find(X509FindType.FindBySubjectName, certSubjectName, validOnly: false);
                if (certs.Count == 0)
                {
                    throw new InvalidOperationException($"Unable to find a certificate with subject name '{certSubjectName}'.");
                }
                
                // Ensure we can safely load the certificate from the store.
                using (certs[0].GetRSAPrivateKey()) { }

                return certs[0];
            }
        }

        //http://stackoverflow.com/questions/4810841/how-can-i-pretty-print-json-using-javascript
        public static void PrintColoredJson(JContainer json)
        {
            const string jsonPatterns =
                @"(\s*""(\\u[a-zA-Z0-9]{4}|\\[^u]|[^\\""])*""(\s*:)?|\s*\b(true|false|null)\b|\s*-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?|\s*[\[\{\]\},]|\s*\n)";
            const ConsoleColor keyColor = ConsoleColor.DarkGreen;
            const ConsoleColor numbersColor = ConsoleColor.Cyan;
            const ConsoleColor stringColor = ConsoleColor.DarkYellow;
            const ConsoleColor booleanColor = ConsoleColor.DarkCyan;
            const ConsoleColor nullColor = ConsoleColor.DarkMagenta;

            var originalColor = Console.ForegroundColor;

            try
            {

                var regex = new Regex(jsonPatterns, RegexOptions.None);

                foreach (Match match in regex.Matches(json.ToString()))
                {
                    if (match.Success)
                    {
                        var value = match.Groups[1].Value;
                        var currentColor = numbersColor;
                        if (Regex.IsMatch(value, "^\\s*\""))
                        {
                            currentColor = Regex.IsMatch(value, ":$") ? keyColor : stringColor;
                        }
                        else if (Regex.IsMatch(value, "true|false"))
                        {
                            currentColor = booleanColor;
                        }
                        else if (Regex.IsMatch(value, "null"))
                        {
                            currentColor = nullColor;
                        }
                        else if (Regex.IsMatch(value, @"[\[\{\]\},]"))
                        {
                            currentColor = originalColor;
                        }

                        Console.ForegroundColor = currentColor;
                        Console.Write(value);
                    }
                }
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }

        public static void PrintColoredXml(string str)
        {
            ConsoleColor HC_NODE = ConsoleColor.DarkGreen;
            ConsoleColor HC_STRING = ConsoleColor.Blue;
            ConsoleColor HC_ATTRIBUTE = ConsoleColor.Red;
            ConsoleColor HC_COMMENT = ConsoleColor.DarkGray;
            ConsoleColor HC_INNERTEXT = ConsoleColor.DarkYellow;

            int cur = 0;
            int k = 0;

            int st, en;
            int lasten = -1;
            while (k < str.Length)
            {
                st = str.IndexOf('<', k);

                if (st < 0)
                    break;

                if (lasten > 0)
                {
                    PrintColor(HC_INNERTEXT, str, lasten + 1, st - lasten - 1, ref cur);
                }

                en = str.IndexOf('>', st + 1);
                if (en < 0)
                    break;

                k = en + 1;
                lasten = en;

                if (str[st + 1] == '!' && str[st + 2] == '-' && str[st + 3] == '-')
                {
                    k = str.IndexOf("-->", st + 3) + 2;
                    PrintColor(HC_COMMENT, str, st + 1, k - st - 1, ref cur);
                    PrintColor(HC_NODE, str, k, 1, ref cur);
                    ++k;
                    lasten = k - 1;
                    continue;

                }
                String nodeText = str.Substring(st + 1, en - st - 1);


                bool inString = false;

                int lastSt = -1;
                int state = 0;
                /* 0 = before node name
                 * 1 = in node name
                   2 = after node name
                   3 = in attribute
                   4 = in string
                   */
                int startNodeName = 0, startAtt = 0;
                for (int i = 0; i < nodeText.Length; ++i)
                {
                    if (nodeText[i] == '"')
                        inString = !inString;

                    if (inString && nodeText[i] == '"')
                        lastSt = i;
                    else
                        if (nodeText[i] == '"')
                    {
                        PrintColor(HC_STRING, str, lastSt + st + 2, i - lastSt - 1, ref cur);
                    }

                    switch (state)
                    {
                        case 0:
                            if (!Char.IsWhiteSpace(nodeText, i))
                            {
                                startNodeName = i;
                                state = 1;
                            }
                            break;
                        case 1:
                            if (Char.IsWhiteSpace(nodeText, i))
                            {
                                PrintColor(HC_NODE, str, startNodeName + st, i - startNodeName + 1, ref cur);
                                state = 2;
                            }
                            break;
                        case 2:
                            if (!Char.IsWhiteSpace(nodeText, i))
                            {
                                startAtt = i;
                                state = 3;
                            }
                            break;

                        case 3:
                            if (Char.IsWhiteSpace(nodeText, i) || nodeText[i] == '=')
                            {
                                PrintColor(HC_ATTRIBUTE, str, startAtt + st, i - startAtt + 1, ref cur);
                                state = 4;
                            }
                            break;
                        case 4:
                            if (nodeText[i] == '"' && !inString)
                                state = 2;
                            break;


                    }

                }

                if (state == 1)
                {
                    PrintColor(HC_NODE, str, st + 1, nodeText.Length, ref cur);
                }
            }

            if (cur < str.Length)
            {
                PrintColor(ConsoleColor.DarkGreen, str, cur, str.Length - cur, ref cur);
            }
        }

        static void PrintColor(ConsoleColor color, string str, int begin, int lenght, ref int cur)
        {
            var originalColor = Console.ForegroundColor;
            try
            {
                if (cur < begin)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.Write(str.Substring(cur, begin - cur));
                }

                Console.ForegroundColor = color;
                Console.Write(str.Substring(begin, lenght));
                cur = begin + lenght;
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }
        }

        static string GetTenantOrSubscription(Uri uri)
        {
            try
            {
                var paths = uri.AbsolutePath.Split(new[] { '/', '?' }, StringSplitOptions.RemoveEmptyEntries);
                if (Utils.IsGraphApi(uri))
                {
                    return paths[0];
                }

                if (paths.Length >= 2 && String.Equals(paths[0], "subscriptions", StringComparison.OrdinalIgnoreCase))
                {
                    return Guid.Parse(paths[1]).ToString();
                }

                Guid subscription;
                if (paths.Length > 0 && Guid.TryParse(paths[0], out subscription))
                {
                    return subscription.ToString();
                }

                Guid unused;
                var loginTenant = Utils.GetLoginTenant();
                if (Guid.TryParse(loginTenant, out unused))
                {
                    return loginTenant;
                }

                return null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(String.Format("Invalid url {0}!", uri), ex);
            }
        }

        static string GetResource(Uri uri, AzureEnvironments env)
        {
            try
            {
                if (Utils.IsGraphApi(uri))
                {
                    return Constants.AADGraphUrls[(int)env];
                }

                if (Utils.IsKeyVault(uri))
                {
                    return Constants.KeyVaultResources[(int)env];
                }

                return Constants.CSMResources[(int)env];
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(String.Format("Invalid url {0}!", uri), ex);
            }
        }

        static AzureEnvironments GetAzureEnvironments(Uri uri, PersistentAuthHelper persistentAuthHelper)
        {
            var host = uri.Host;
            
            var graphs = Constants.AADGraphUrls.Where(url => url.IndexOf(host, StringComparison.OrdinalIgnoreCase) > 0);
            if (graphs.Count() > 1)
            {
                var env = persistentAuthHelper.AzureEnvironments;
                if (Constants.AADGraphUrls[(int)env].IndexOf(host, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    return env;
                }

                env = Utils.GetDefaultEnv();
                if (Constants.AADGraphUrls[(int)env].IndexOf(host, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    return env;
                }
            }

            for (int i = 0; i < Constants.AADGraphUrls.Length; ++i)
            {
                var url = Constants.AADGraphUrls[i];
                if (url.IndexOf(host, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    return (AzureEnvironments)i;
                }
            }

            for (int i = 0; i < Constants.CSMUrls.Length; ++i)
            {
                var urls = Constants.CSMUrls[i];
                if (urls.Any(url => url.IndexOf(host, StringComparison.OrdinalIgnoreCase) > 0))
                {
                    return (AzureEnvironments)i;
                }
            }

            for (int i = 0; i < Constants.RdfeUrls.Length; ++i)
            {
                var url = Constants.RdfeUrls[i];
                if (url.IndexOf(host, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    return (AzureEnvironments)i;
                }
            }

            for (int i = 0; i < Constants.SCMSuffixes.Length; ++i)
            {
                var suffix = Constants.SCMSuffixes[i];
                if (host.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    return (AzureEnvironments)i;
                }
            }

            for (int i = 0; i < Constants.VsoSuffixes.Length; ++i)
            {
                var suffix = Constants.VsoSuffixes[i];
                if (host.IndexOf(suffix, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    return (AzureEnvironments)i;
                }
            }

            if (Utils.IsKeyVault(uri))
            {
                return AzureEnvironments.Prod;
            }
            
            if (Utils.IsCustom(uri))
            {
                return Utils.GetDefaultEnv(AzureEnvironments.Dogfood);
            }

            return Utils.GetDefaultEnv();
        }
    }
}
