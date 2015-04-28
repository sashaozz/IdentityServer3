using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using Thinktecture.IdentityServer.Core.Models;

namespace Thinktecture.IdentityServer.Core.Services
{
    /// <summary>
    /// 
    /// </summary>
    public interface IAuthenticationManager
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="signInMessage"></param>
        /// <param name="signin"></param>
        /// <returns></returns>
        Task<AuthenticateResponse> ProcessAuthenticationAsync(SignInMessage signInMessage, string signin);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="externalUser"></param>
        /// <param name="signInMessage"></param>
        /// <param name="signin"></param>
        /// <param name="error"></param>
        /// <returns></returns>
        Task<AuthenticateResponse> ProcessExternalAuthenticationCallbackAsync(ExternalIdentity externalUser, SignInMessage signInMessage, string signin, string error);
    }
}
