using System.Threading.Tasks;
using System.Web.Http;
using Thinktecture.IdentityServer.Core.Logging;
using Thinktecture.IdentityServer.Core.Models;

namespace Thinktecture.IdentityServer.Core.Services
{
    /// <summary>
    /// 
    /// </summary>
    public class DefaultAuthenticationManager : IAuthenticationManager
    {
        private readonly static ILog Logger = LogProvider.GetCurrentClassLogger();

        public Task<AuthenticateResponse> ProcessAuthenticationAsync(SignInMessage signInMessage, string signin)
        {
            return Task.FromResult<AuthenticateResponse>(null);
        }

        public Task<AuthenticateResponse> ProcessExternalAuthenticationCallbackAsync(ExternalIdentity externalUser, SignInMessage signInMessage,
            string signin, string error)
        {
            return Task.FromResult<AuthenticateResponse>(null);
        }
    }

}
