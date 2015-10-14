using System.Collections.Generic;
using System.Threading.Tasks;

namespace Thinktecture.IdentityServer.Core.Services
{
    /// <summary>
    /// 
    /// </summary>
    public interface ISsoCheckService
    {
        Task<bool> NeedSetSsoCookieAsync();
    }
}
