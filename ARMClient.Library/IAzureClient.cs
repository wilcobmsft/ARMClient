using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace ARMClient.Library
{
    interface IAzureClient
    {
        void ConfigureSpnLogin(string tenantId, string appId, string appKey);
        void ConfigureUpnLogin(string userName, string password);
        Task<HttpResponseMessage> HttpInvoke(HttpMethod method, Uri uri, object objectPayload);
        Task<HttpResponseMessage> HttpInvoke(string method, Uri uri, object objectPayload);
    }
}
