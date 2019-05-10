using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ARMClient.Authentication.Contracts;

namespace ARMClient.Authentication
{
    public interface IAuthHelper
    {
        AzureEnvironments AzureEnvironments { get; set; }
        Task AcquireTokens(string resource = null);
        Task AzLogin();
        Task<TokenCacheInfo> GetToken(string id, string resource);
        Task<TokenCacheInfo> GetTokenBySpn(string tenantId, string appId, string appKey, string resource = null);
        Task<TokenCacheInfo> GetTokenByUpn(string username, string password);
        bool IsCacheValid();
        void ClearTokenCache();
        IEnumerable<string> DumpTokenCache();
    }
}
