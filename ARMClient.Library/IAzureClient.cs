using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace ARMClient.Library
{
    public interface IAzureClient
    {
        void ConfigureSpnLogin(string tenantId, string appId, string appKey);
        void ConfigureUpnLogin(string userName, string password);
        Task<HttpResponseMessage> HttpInvoke(HttpMethod method, Uri uri, object objectPayload = null);
        Task<HttpResponseMessage> HttpInvoke(string method, Uri uri, object objectPayload = null);
    }
}
