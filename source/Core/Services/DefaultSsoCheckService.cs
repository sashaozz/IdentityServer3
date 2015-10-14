using System.Threading.Tasks;

namespace Thinktecture.IdentityServer.Core.Services
{
    /// <summary>
    /// 
    /// </summary>
    public class DefaultSsoCheckService : ISsoCheckService
    {
        public Task<bool> NeedSetSsoCookieAsync()
        {
            return Task.FromResult(false);
        }
    }
}
