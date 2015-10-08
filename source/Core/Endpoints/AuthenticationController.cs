﻿/*
 * Copyright 2014, 2015 Dominick Baier, Brock Allen
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Microsoft.Owin;
using Newtonsoft.Json;
using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Web.Http;
using Thinktecture.IdentityModel;
using Thinktecture.IdentityModel.Extensions;
using Thinktecture.IdentityServer.Core.Configuration;
using Thinktecture.IdentityServer.Core.Configuration.Hosting;
using Thinktecture.IdentityServer.Core.Events;
using Thinktecture.IdentityServer.Core.Extensions;
using Thinktecture.IdentityServer.Core.Logging;
using Thinktecture.IdentityServer.Core.Models;
using Thinktecture.IdentityServer.Core.Resources;
using Thinktecture.IdentityServer.Core.Results;
using Thinktecture.IdentityServer.Core.Services;
using Thinktecture.IdentityServer.Core.Validation;
using Thinktecture.IdentityServer.Core.ViewModels;

#pragma warning disable 1591

namespace Thinktecture.IdentityServer.Core.Endpoints
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    [ErrorPageFilter]
    [SecurityHeaders]
    [NoCache]
    [PreventUnsupportedRequestMediaTypes(allowFormUrlEncoded: true)]
    [HostAuthentication(Constants.PrimaryAuthenticationType)]
    internal class AuthenticationController : ApiController
    {
        public const int MaxInputParamLength = 100;

        private readonly static ILog Logger = LogProvider.GetCurrentClassLogger();

        private readonly IOwinContext context;
        private readonly IViewService viewService;
        private readonly IUserService userService;
        private readonly IdentityServerOptions options;
        private readonly IClientStore clientStore;
        private readonly IEventService eventService;
        private readonly ILocalizationService localizationService;
        private readonly SessionCookie sessionCookie;
        private readonly MessageCookie<SignInMessage> signInMessageCookie;
        private readonly MessageCookie<SignOutMessage> signOutMessageCookie;
        private readonly LastUserNameCookie lastUserNameCookie;
        private readonly AntiForgeryToken antiForgeryToken;
        private readonly IAuthenticationManager authenticationManager;

        public AuthenticationController(
            OwinEnvironmentService owin,
            IViewService viewService,
            IUserService userService,
            IdentityServerOptions idSvrOptions,
            IClientStore clientStore,
            IEventService eventService,
            ILocalizationService localizationService,
            SessionCookie sessionCookie,
            MessageCookie<SignInMessage> signInMessageCookie,
            MessageCookie<SignOutMessage> signOutMessageCookie,
            LastUserNameCookie lastUsernameCookie,
            AntiForgeryToken antiForgeryToken,
            IAuthenticationManager authenticationManager)
        {
            this.context = new OwinContext(owin.Environment);
            this.viewService = viewService;
            this.userService = userService;
            this.options = idSvrOptions;
            this.clientStore = clientStore;
            this.eventService = eventService;
            this.localizationService = localizationService;
            this.sessionCookie = sessionCookie;
            this.signInMessageCookie = signInMessageCookie;
            this.signOutMessageCookie = signOutMessageCookie;
            this.lastUserNameCookie = lastUsernameCookie;
            this.antiForgeryToken = antiForgeryToken;
            this.authenticationManager = authenticationManager;
        }

        [Route(Constants.RoutePaths.Login, Name = Constants.RouteNames.Login)]
        [HttpGet]
        public async Task<IHttpActionResult> Login(string signin = null)
        {
            Logger.Info("Login page requested");

            if (signin.IsMissing())
            {
                Logger.Error("No signin id passed");
                return HandleNoSignin();
            }

            if (signin.Length > MaxInputParamLength)
            {
                Logger.Error("Signin parameter passed was larger than max length");
                return RenderErrorPage();
            }

            var signInMessage = signInMessageCookie.Read(signin);
            if (signInMessage == null)
            {
                Logger.Error("No cookie matching signin id found");
                return HandleNoSignin();
            }

            Logger.DebugFormat("signin message passed to login: {0}", JsonConvert.SerializeObject(signInMessage, Formatting.Indented));

            var authResult = await userService.PreAuthenticateAsync(signInMessage);
            if (authResult != null)
            {
                if (authResult.IsError)
                {
                    Logger.WarnFormat("user service returned an error message: {0}", authResult.ErrorMessage);

                    eventService.RaisePreLoginFailureEvent(signin, signInMessage, authResult.ErrorMessage);

                    return RenderErrorPage(authResult.ErrorMessage);
                }

                Logger.Info("user service returned a login result");

                eventService.RaisePreLoginSuccessEvent(signin, signInMessage, authResult);

                var rememberMe = false;
                if (options.AuthenticationOptions.CookieOptions.AllowRememberMe)
                {
                    Logger.Info("Try get AcrValue fpcc-remember_me");
                    string rememberMeAcrValue;
                    if (signInMessage.TryGetAcrValue("fpcc-remember_me", out rememberMeAcrValue))
                    {
                        Logger.Info(string.Format("The result of the attempt to take the value fpcc-remember_me: {0}", rememberMeAcrValue));

                        bool.TryParse(rememberMeAcrValue, out rememberMe);
                    }
                }

                return SignInAndRedirect(signInMessage, signin, authResult, 
                    options.AuthenticationOptions.CookieOptions.AllowRememberMe ? rememberMe : (bool?)null);
            }

            if (signInMessage.IdP.IsPresent())
            {
                Logger.InfoFormat("identity provider requested, redirecting to: {0}", signInMessage.IdP);
                return Redirect(context.GetExternalProviderLoginUrl(signInMessage.IdP, signin));
            }

            var result = ProcessAuthResponse(await authenticationManager.ProcessAuthenticationAsync(signInMessage, signin));
            if (result != null) return result;

            return await RenderLoginPage(signInMessage, signin);
        }

        [Route(Constants.RoutePaths.Login)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IHttpActionResult> LoginLocal(string signin, LoginCredentials model)
        {
            Logger.Info("Login page submitted");

            if (this.options.AuthenticationOptions.EnableLocalLogin == false)
            {
                Logger.Warn("EnableLocalLogin disabled -- returning 405 MethodNotAllowed");
                return StatusCode(HttpStatusCode.MethodNotAllowed);
            }

            if (signin.IsMissing())
            {
                Logger.Error("No signin id passed");
                return HandleNoSignin();
            }

            if (signin.Length > MaxInputParamLength)
            {
                Logger.Error("Signin parameter passed was larger than max length");
                return RenderErrorPage();
            }

            var signInMessage = signInMessageCookie.Read(signin);
            if (signInMessage == null)
            {
                Logger.Error("No cookie matching signin id found");
                return HandleNoSignin();
            }

            if (!(await IsLocalLoginAllowedForClient(signInMessage)))
            {
                Logger.ErrorFormat("Login not allowed for client {0}", signInMessage.ClientId);
                return RenderErrorPage();
            }

            if (model == null)
            {
                Logger.Error("no data submitted");
                return await RenderLoginPage(signInMessage, signin, localizationService.GetMessage(MessageIds.InvalidUsernameOrPassword));
            }

            if (String.IsNullOrWhiteSpace(model.Username))
            {
                ModelState.AddModelError("Username", localizationService.GetMessage(MessageIds.UsernameRequired));
            }

            if (String.IsNullOrWhiteSpace(model.Password))
            {
                ModelState.AddModelError("Password", localizationService.GetMessage(MessageIds.PasswordRequired));
            }

            model.RememberMe = options.AuthenticationOptions.CookieOptions.CalculateRememberMeFromUserInput(model.RememberMe);

            if (!ModelState.IsValid)
            {
                Logger.Warn("validation error: username or password missing");
                return await RenderLoginPage(signInMessage, signin, ModelState.GetError(), model.Username, model.RememberMe == true);
            }

            if (model.Username.Length > MaxInputParamLength || model.Password.Length > MaxInputParamLength)
            {
                Logger.Error("username or password submitted beyond allowed length");
                return await RenderLoginPage(signInMessage, signin);
            }

            var authResult = await userService.AuthenticateLocalAsync(model.Username, model.Password, signInMessage);
            if (authResult == null)
            {
                Logger.WarnFormat("user service indicated incorrect username or password for username: {0}", model.Username);

                var errorMessage = localizationService.GetMessage(MessageIds.InvalidUsernameOrPassword);
                eventService.RaiseLocalLoginFailureEvent(model.Username, signin, signInMessage, errorMessage);

                return await RenderLoginPage(signInMessage, signin, errorMessage, model.Username, model.RememberMe == true);
            }

            if (authResult.IsError)
            {
                Logger.WarnFormat("user service returned an error message: {0}", authResult.ErrorMessage);

                eventService.RaiseLocalLoginFailureEvent(model.Username, signin, signInMessage, authResult.ErrorMessage);

                return await RenderLoginPage(signInMessage, signin, authResult.ErrorMessage, model.Username, model.RememberMe == true);
            }

            Logger.Info("Login credentials successfully validated by user service");

            eventService.RaiseLocalLoginSuccessEvent(model.Username, signin, signInMessage, authResult);

            lastUserNameCookie.SetValue(model.Username);

            return SignInAndRedirect(signInMessage, signin, authResult, model.RememberMe);
        }

        [Route(Constants.RoutePaths.LoginExternal, Name = Constants.RouteNames.LoginExternal)]
        [HttpGet]
        public async Task<IHttpActionResult> LoginExternal(string signin, string provider)
        {
            Logger.InfoFormat("External login requested for provider: {0}", provider);

            if (provider.IsMissing())
            {
                Logger.Error("No provider passed");
                return RenderErrorPage(localizationService.GetMessage(MessageIds.NoExternalProvider));
            }

            if (provider.Length > MaxInputParamLength)
            {
                Logger.Error("Provider parameter passed was larger than max length");
                return RenderErrorPage();
            }

            if (signin.IsMissing())
            {
                Logger.Error("No signin id passed");
                return HandleNoSignin();
            }

            if (signin.Length > MaxInputParamLength)
            {
                Logger.Error("Signin parameter passed was larger than max length");
                return RenderErrorPage();
            }

            var signInMessage = signInMessageCookie.Read(signin);
            if (signInMessage == null)
            {
                Logger.Error("No cookie matching signin id found");
                return HandleNoSignin();
            }

            if (!(await clientStore.IsValidIdentityProviderAsync(signInMessage.ClientId, provider)))
            {
                var msg = String.Format("External login error: provider {0} not allowed for client: {1}", provider, signInMessage.ClientId);
                Logger.ErrorFormat(msg);
                eventService.RaiseFailureEndpointEvent(EventConstants.EndpointNames.Authenticate, msg);
                return RenderErrorPage();
            }

            if (context.IsValidExternalAuthenticationProvider(provider) == false)
            {
                var msg = String.Format("External login error: provider requested {0} is not a configured external provider", provider);
                Logger.ErrorFormat(msg);
                eventService.RaiseFailureEndpointEvent(EventConstants.EndpointNames.Authenticate, msg);
                return RenderErrorPage();
            }

            var authProp = new Microsoft.Owin.Security.AuthenticationProperties
            {
                RedirectUri = Url.Route(Constants.RouteNames.LoginExternalCallback, null)
            };

            Logger.Info("Triggering challenge for external identity provider");

            // add the id to the dictionary so we can recall the cookie id on the callback
            authProp.Dictionary.Add(Constants.Authentication.SigninId, signin);
            authProp.Dictionary.Add(Constants.Authentication.KatanaAuthenticationType, provider);
            context.Authentication.Challenge(authProp, provider);

            return Unauthorized();
        }

        private IHttpActionResult ProcessAuthResponse(AuthenticateResponse response)
        {
            if (response is AuthErrorResponse)
            {
                return RenderErrorPage((response as AuthErrorResponse).Message);
            }

            if (response is RedirectToIdpResponse)
            {
                var redirect = response as RedirectToIdpResponse;
                return Redirect(context.GetExternalProviderLoginUrl(redirect.IdP, redirect.Signin));
            }

            if (response is RedirectResponse)
            {
                var redirect = response as RedirectResponse;
                return Redirect(redirect.RedirectPath);
            }

            return null;
        }

        [Route(Constants.RoutePaths.LoginExternalCallback, Name = Constants.RouteNames.LoginExternalCallback)]
        [HttpGet]
        public async Task<IHttpActionResult> LoginExternalCallback(string error = null)
        {
            Logger.Info("Callback invoked from external identity provider");

            if (error.IsPresent())
            {
                if (error.Length > MaxInputParamLength) error = error.Substring(0, MaxInputParamLength);

                Logger.ErrorFormat("External identity provider returned error: {0}", error);
                eventService.RaiseExternalLoginErrorEvent(error);

                return RenderErrorPage(String.Format(localizationService.GetMessage(MessageIds.ExternalProviderError), error));
            }

            var signInId = await context.GetSignInIdFromExternalProvider();
            if (signInId.IsMissing())
            {
                Logger.Error("No signin id passed");
                return HandleNoSignin();
            }

            var signInMessage = signInMessageCookie.Read(signInId);
            if (signInMessage == null)
            {
                Logger.Error("No cookie matching signin id found");
                return HandleNoSignin();
            }

            var returnUnsuccessfulResultAcrValue = "";
            var returnUnsuccessfulResult = false;
            if (signInMessage.TryGetAcrValue("return_unsuccessful_result", out returnUnsuccessfulResultAcrValue))
            {
                Logger.Info(string.Format(""));
                bool.TryParse(returnUnsuccessfulResultAcrValue, out returnUnsuccessfulResult);
            }

            Logger.InfoFormat("returnUnsuccessfulResultAcrValue: {0}, returnUnsuccessfulResult: {1}", returnUnsuccessfulResultAcrValue, returnUnsuccessfulResult);

            var user = await context.GetIdentityFromExternalProvider();
            var externalIdentity = (user != null) ? ExternalIdentity.FromClaims(user.Claims) : null;

            var result = ProcessAuthResponse(await authenticationManager.ProcessExternalAuthenticationCallbackAsync(externalIdentity, signInMessage, signInId, error));
            if (result != null)
            {
                return result;
            }

            if (user == null)
            {
                /////////////////
                ////////////////
                Logger.Error("no identity from external identity provider");
                return await RenderLoginPage(signInMessage, signInId, localizationService.GetMessage(MessageIds.NoMatchingExternalAccount));
            }

            if (externalIdentity == null)
            {
                /////////////////
                ////////////////
                var claims = user.Claims.Select(x => new { x.Type, x.Value });
                Logger.ErrorFormat("no subject or unique identifier claims from external identity provider. Claims provided:\r\n{0}", LogSerializer.Serialize(claims));
                return await RenderLoginPage(signInMessage, signInId, localizationService.GetMessage(MessageIds.NoMatchingExternalAccount));
            }

            Logger.InfoFormat("external user provider: {0}, provider ID: {1}", externalIdentity.Provider, externalIdentity.ProviderId);

            AuthenticateResult authResult = null;
            try
            {
                authResult = await userService.AuthenticateExternalAsync(externalIdentity, signInMessage);
            }
            catch (Exception ex)
            {
                if (!returnUnsuccessfulResult)
                {
                    throw ex;
                }

                return AuthorizeError(Constants.AuthorizeErrors.LoginRequired, signInMessage, options);
            }

            if (authResult == null)
            {
                Logger.Warn("user service failed to authenticate external identity");

                var msg = localizationService.GetMessage(MessageIds.NoMatchingExternalAccount);
                eventService.RaiseExternalLoginFailureEvent(externalIdentity, signInId, signInMessage, msg);

                if (!returnUnsuccessfulResult)
                {
                    return await RenderLoginPage(signInMessage, signInId, msg);
                }

                return AuthorizeError(Constants.AuthorizeErrors.LoginRequired, signInMessage, options);
            }

            if (authResult.IsError)
            {
                Logger.WarnFormat("user service returned error message: {0}", authResult.ErrorMessage);

                eventService.RaiseExternalLoginFailureEvent(externalIdentity, signInId, signInMessage, authResult.ErrorMessage);

                if (!returnUnsuccessfulResult)
                {
                    return await RenderLoginPage(signInMessage, signInId, authResult.ErrorMessage);
                }

                return AuthorizeError(Constants.AuthorizeErrors.ServerError, signInMessage, options);
            }

            Logger.Info("External identity successfully validated by user service");

            eventService.RaiseExternalLoginSuccessEvent(externalIdentity, signInId, signInMessage, authResult);

            return SignInAndRedirect(signInMessage, signInId, authResult);
        }

        [Route(Constants.RoutePaths.ResumeLoginFromRedirect, Name = Constants.RouteNames.ResumeLoginFromRedirect)]
        [HttpGet]
        public async Task<IHttpActionResult> ResumeLoginFromRedirect(string resume)
        {
            Logger.Info("Callback requested to resume login from partial login");

            if (resume.IsMissing())
            {
                Logger.Error("no resumeId passed");
                return RenderErrorPage();
            }

            if (resume.Length > MaxInputParamLength)
            {
                Logger.Error("resumeId length longer than allowed length");
                return RenderErrorPage();
            }

            var user = await context.GetIdentityFromPartialSignIn();
            if (user == null)
            {
                Logger.Error("no identity from partial login");
                return RenderErrorPage();
            }

            var type = GetClaimTypeForResumeId(resume);
            var resumeClaim = user.FindFirst(type);
            if (resumeClaim == null)
            {
                Logger.Error("no claim matching resumeId");
                return RenderErrorPage();
            }

            var signInId = resumeClaim.Value;
            if (signInId.IsMissing())
            {
                Logger.Error("No signin id found in resume claim");
                return RenderErrorPage();
            }

            var signInMessage = signInMessageCookie.Read(signInId);
            if (signInMessage == null)
            {
                Logger.Error("No cookie matching signin id found");
                return RenderErrorPage();
            }

            // check to see if the partial login has all the claim types needed to login
            AuthenticateResult result = null;
            if (Constants.AuthenticateResultClaimTypes.All(claimType => user.HasClaim(claimType)))
            {
                Logger.Info("Authentication claims found -- logging user in");

                // the user/subject was known, so pass thru (without the redirect claims)
                if (user.HasClaim(Constants.ClaimTypes.PartialLoginReturnUrl))
                {
                    user.RemoveClaim(user.FindFirst(Constants.ClaimTypes.PartialLoginReturnUrl));
                }
                if (user.HasClaim(Constants.ClaimTypes.ExternalProviderUserId))
                {
                    user.RemoveClaim(user.FindFirst(Constants.ClaimTypes.ExternalProviderUserId));
                }
                if (user.HasClaim(GetClaimTypeForResumeId(resume)))
                {
                    user.RemoveClaim(user.FindFirst(GetClaimTypeForResumeId(resume)));
                }

                result = new AuthenticateResult(new ClaimsPrincipal(user));

                eventService.RaisePartialLoginCompleteEvent(user, signInId, signInMessage);
            }
            else
            {
                Logger.Info("Authentication claims not found -- looking for ExternalProviderUserId to call AuthenticateExternalAsync");

                // the user was not known, we need to re-execute AuthenticateExternalAsync
                // to obtain a subject to proceed
                var externalProviderClaim = user.FindFirst(Constants.ClaimTypes.ExternalProviderUserId);
                if (externalProviderClaim == null)
                {
                    Logger.Error("No ExternalProviderUserId claim found -- rendering error page");
                    return RenderErrorPage();
                }

                var provider = externalProviderClaim.Issuer;
                var providerId = externalProviderClaim.Value;
                var externalId = new ExternalIdentity
                {
                    Provider = provider,
                    ProviderId = providerId,
                    Claims = user.Claims
                };

                Logger.InfoFormat("external user provider: {0}, provider ID: {1}", externalId.Provider, externalId.ProviderId);

                result = await userService.AuthenticateExternalAsync(externalId, signInMessage);

                if (result == null)
                {
                    Logger.Warn("user service failed to authenticate external identity");

                    var msg = localizationService.GetMessage(MessageIds.NoMatchingExternalAccount);
                    eventService.RaiseExternalLoginFailureEvent(externalId, signInId, signInMessage, msg);

                    return await RenderLoginPage(signInMessage, signInId, msg);
                }

                if (result.IsError)
                {
                    Logger.WarnFormat("user service returned error message: {0}", result.ErrorMessage);

                    eventService.RaiseExternalLoginFailureEvent(externalId, signInId, signInMessage, result.ErrorMessage);

                    return await RenderLoginPage(signInMessage, signInId, result.ErrorMessage);
                }

                Logger.Info("External identity successfully validated by user service");

                eventService.RaiseExternalLoginSuccessEvent(externalId, signInId, signInMessage, result);
            }

            return SignInAndRedirect(signInMessage, signInId, result);
        }

        [Route(Constants.RoutePaths.Logout, Name = Constants.RouteNames.LogoutPrompt)]
        [HttpGet]
        public async Task<IHttpActionResult> LogoutPrompt(string id = null)
        {
            if (id != null && id.Length > MaxInputParamLength)
            {
                Logger.Error("Logout prompt requested, but id param is longer than allowed length");
                return RenderErrorPage();
            }

            var user = (ClaimsPrincipal)User;
            if (user == null || user.Identity.IsAuthenticated == false)
            {
                // user is already logged out, so just trigger logout cleanup
                return await Logout(id);
            }

            var sub = user.GetSubjectId();
            Logger.InfoFormat("Logout prompt for subject: {0}", sub);

            var message = signOutMessageCookie.Read(id);
            if (message != null && message.ClientId.IsPresent())
            {
                Logger.InfoFormat("SignOutMessage present (from client {0}), performing logout", message.ClientId);
                return await Logout(id);
            }

            if (!this.options.AuthenticationOptions.EnableSignOutPrompt)
            {
                Logger.InfoFormat("EnableSignOutPrompt set to false, performing logout");
                return await Logout(id);
            }

            Logger.InfoFormat("EnableSignOutPrompt set to true, rendering logout prompt");
            return RenderLogoutPromptPage();
        }

        [Route(Constants.RoutePaths.Logout, Name = Constants.RouteNames.Logout)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IHttpActionResult> Logout(string id = null)
        {
            Logger.Info("Logout endpoint submitted");

            if (id != null && id.Length > MaxInputParamLength)
            {
                Logger.Error("id param is longer than allowed length");
                return RenderErrorPage();
            }

            var user = (ClaimsPrincipal)User;
            if (user != null && user.Identity.IsAuthenticated)
            {
                var sub = user.GetSubjectId();
                Logger.InfoFormat("Logout requested for subject: {0}", sub);
            }

            Logger.Info("Clearing cookies");

            sessionCookie.ClearSessionId();
            signOutMessageCookie.Clear(id);

            ClearAuthenticationCookies();
            SignOutOfExternalIdP();

            if (user != null && user.Identity.IsAuthenticated)
            {
                await this.userService.SignOutAsync(user);

                var message = signOutMessageCookie.Read(id);
                eventService.RaiseLogoutEvent(user, id, message);
            }

            return await RenderLoggedOutPage(id);
        }

        private IHttpActionResult HandleNoSignin()
        {
            if (options.AuthenticationOptions.InvalidSignInRedirectUrl.IsMissing())
            {
                return RenderErrorPage(localizationService.GetMessage(MessageIds.NoSignInCookie));
            }

            var url = options.AuthenticationOptions.InvalidSignInRedirectUrl;
            if (url.StartsWith("~/"))
            {
                url = url.Substring(2);
                url = Request.GetOwinEnvironment().GetIdentityServerBaseUrl() + url;
            }
            else if (url.StartsWith("/"))
            {
                url = Request.GetOwinEnvironment().GetIdentityServerHost() + url;
            }
            else
            {
                url = options.AuthenticationOptions.InvalidSignInRedirectUrl;
            }

            return Redirect(url);
        }

        private IHttpActionResult SignInAndRedirect(SignInMessage signInMessage, string signInMessageId, AuthenticateResult authResult, bool? rememberMe = null)
        {
            ClearAuthenticationCookies();
            IssueAuthenticationCookie(signInMessageId, authResult, rememberMe);

            var redirectUrl = GetRedirectUrl(signInMessage, authResult);
            Logger.InfoFormat("redirecting to: {0}", redirectUrl);
            return Redirect(redirectUrl);
        }

        private void IssueAuthenticationCookie(string signInMessageId, AuthenticateResult authResult, bool? rememberMe = null)
        {
            if (authResult == null) throw new ArgumentNullException("authResult");

            if (authResult.IsPartialSignIn)
            {
                Logger.Info("issuing partial signin cookie");
            }
            else
            {
                Logger.Info("issuing primary signin cookie");
            }

            var props = new Microsoft.Owin.Security.AuthenticationProperties();

            var id = authResult.User.Identities.First();
            if (authResult.IsPartialSignIn)
            {
                // add claim so partial redirect can return here to continue login
                // we need a random ID to resume, and this will be the query string
                // to match a claim added. the claim added will be the original 
                // signIn ID. 
                var resumeId = CryptoRandom.CreateUniqueId();

                var resumeLoginUrl = context.GetPartialLoginResumeUrl(resumeId);
                var resumeLoginClaim = new Claim(Constants.ClaimTypes.PartialLoginReturnUrl, resumeLoginUrl);
                id.AddClaim(resumeLoginClaim);
                id.AddClaim(new Claim(GetClaimTypeForResumeId(resumeId), signInMessageId));

                if (rememberMe.HasValue)
                {
                    Logger.Info("IssueAuthenticationCookie issuing rememberMe.Value" + rememberMe.Value);
                    id.AddClaim(new Claim(Constants.ClaimTypes.PartialRememberMe, rememberMe.Value.ToString()));
                }
            }
            else
            {
                var rememberMeClaim = id.Claims.FirstOrDefault(c => c.Type == Constants.ClaimTypes.PartialRememberMe);

                if (rememberMeClaim != null)
                {
                    Logger.Info("IssueAuthenticationCookie rememberMeClaim != null");
                    Logger.Info("IssueAuthenticationCookie rememberMeClaim.val " + rememberMeClaim);
                    var rememberMeParseResult = false;
                    if (bool.TryParse(rememberMeClaim.Value, out rememberMeParseResult))
                    {
                        rememberMe = rememberMeParseResult;
                    }
                }

                signInMessageCookie.Clear(signInMessageId);
                sessionCookie.IssueSessionId(rememberMe);
            }

            if (!authResult.IsPartialSignIn)
            {
                // don't issue persistnt cookie if it's a partial signin
                if (rememberMe == true ||
                    (rememberMe != false && this.options.AuthenticationOptions.CookieOptions.IsPersistent))
                {
                    // only issue persistent cookie if user consents (rememberMe == true) or
                    // if server is configured to issue persistent cookies and user has not explicitly
                    // denied the rememberMe (false)
                    // if rememberMe is null, then user was not prompted for rememberMe
                    props.IsPersistent = true;
                    if (rememberMe == true)
                    {
                        var expires = DateTimeHelper.UtcNow.Add(options.AuthenticationOptions.CookieOptions.RememberMeDuration);
                        props.ExpiresUtc = new DateTimeOffset(expires);
                    }
                }
            }

            context.Authentication.SignIn(props, id);
        }

        private static string GetClaimTypeForResumeId(string resume)
        {
            return String.Format(Constants.ClaimTypes.PartialLoginResumeId, resume);
        }

        private Uri GetRedirectUrl(SignInMessage signInMessage, AuthenticateResult authResult)
        {
            if (signInMessage == null) throw new ArgumentNullException("signInMessage");
            if (authResult == null) throw new ArgumentNullException("authResult");

            if (authResult.IsPartialSignIn)
            {
                var path = authResult.PartialSignInRedirectPath;
                if (path.StartsWith("~/"))
                {
                    path = path.Substring(2);
                    path = Request.GetIdentityServerBaseUrl() + path;
                }
                var host = new Uri(context.GetIdentityServerHost());
                return new Uri(host, path);
            }
            else
            {
                return new Uri(signInMessage.ReturnUrl);
            }
        }

        private void ClearAuthenticationCookies()
        {
            context.Authentication.SignOut(
                Constants.PrimaryAuthenticationType,
                Constants.ExternalAuthenticationType,
                Constants.PartialSignInAuthenticationType);
        }

        private void SignOutOfExternalIdP()
        {
            // look for idp claim other than IdSvr
            // if present, then signout of it
            var user = User as ClaimsPrincipal;
            if (user != null && user.Identity.IsAuthenticated)
            {
                var idp = user.GetIdentityProvider();
                if (idp != Constants.BuiltInIdentityProvider)
                {
                    context.Authentication.SignOut(idp);
                }
            }
        }



        private async Task<IHttpActionResult> RenderLoginPage(SignInMessage message, string signInMessageId, string errorMessage = null, string username = null, bool? rememberMe = null)
        {
            if (message == null) throw new ArgumentNullException("message");

            username = GetUserNameForLoginPage(message, username);

            var isLocalLoginAllowedForClient = await IsLocalLoginAllowedForClient(message);
            var isLocalLoginAllowed = isLocalLoginAllowedForClient && options.AuthenticationOptions.EnableLocalLogin;

            var idpRestrictions = await clientStore.GetIdentityProviderRestrictionsAsync(message.ClientId);
            var providers = context.GetExternalAuthenticationProviders(idpRestrictions);
            var providerLinks = context.GetLinksFromProviders(providers, signInMessageId);
            var visibleLinks = providerLinks.FilterHiddenLinks();

            if (errorMessage != null)
            {
                Logger.InfoFormat("rendering login page with error message: {0}", errorMessage);
            }
            else
            {
                if (isLocalLoginAllowed == false)
                {
                    if (options.AuthenticationOptions.EnableLocalLogin)
                    {
                        Logger.Info("local login disabled");
                    }
                    if (isLocalLoginAllowedForClient)
                    {
                        Logger.Info("local login disabled for the client");
                    }

                    string url = null;

                    if (!providerLinks.Any())
                    {
                        Logger.Info("no providers registered for client");
                        return RenderErrorPage();
                    }
                    else if (providerLinks.Count() == 1)
                    {
                        Logger.Info("only one provider for client");
                        url = providerLinks.First().Href;
                    }
                    else if (visibleLinks.Count() == 1)
                    {
                        Logger.Info("only one visible provider");
                        url = visibleLinks.First().Href;
                    }

                    if (url.IsPresent())
                    {
                        Logger.InfoFormat("redirecting to provider URL: {0}", url);
                        return Redirect(url);
                    }
                }

                Logger.Info("rendering login page");
            }

            var loginPageLinks = options.AuthenticationOptions.LoginPageLinks.Render(Request.GetIdentityServerBaseUrl(), signInMessageId);

            var rememberMeAcrValue = "";
            if (!rememberMe.HasValue && message.TryGetAcrValue("fpcc-remember_me", out rememberMeAcrValue))
            {
                bool parseResilt;
                bool.TryParse(rememberMeAcrValue, out parseResilt);
                rememberMe = parseResilt;
            }

            var loginModel = new LoginViewModel
            {
                RequestId = context.GetRequestId(),
                SiteName = options.SiteName,
                SiteUrl = Request.GetIdentityServerBaseUrl(),
                ExternalProviders = visibleLinks,
                AdditionalLinks = loginPageLinks,
                ErrorMessage = errorMessage,
                LoginUrl = isLocalLoginAllowed ? Url.Route(Constants.RouteNames.Login, new { signin = signInMessageId }) : null,
                AllowRememberMe = options.AuthenticationOptions.CookieOptions.AllowRememberMe,
                RememberMe = options.AuthenticationOptions.CookieOptions.AllowRememberMe && (rememberMe ?? false),
                CurrentUser = context.GetCurrentUserDisplayName(),
                LogoutUrl = context.GetIdentityServerLogoutUrl(),
                AntiForgery = antiForgeryToken.GetAntiForgeryToken(),
                Username = username
            };

            return new LoginActionResult(viewService, loginModel, message);
        }

        private string GetUserNameForLoginPage(SignInMessage message, string username)
        {
            if (username.IsMissing() && message.LoginHint.IsPresent())
            {
                if (options.AuthenticationOptions.EnableLoginHint)
                {
                    Logger.InfoFormat("Using LoginHint for username: {0}", message.LoginHint);
                    username = message.LoginHint;
                }
                else
                {
                    Logger.Warn("Not using LoginHint because EnableLoginHint is false");
                }
            }

            var lastUsernameCookieValue = lastUserNameCookie.GetValue();
            if (username.IsMissing() && lastUsernameCookieValue.IsPresent())
            {
                Logger.InfoFormat("Using LastUserNameCookie value for username: {0}", lastUsernameCookieValue);
                username = lastUsernameCookieValue;
            }
            return username;
        }

        private async Task<bool> IsLocalLoginAllowedForClient(SignInMessage message)
        {
            if (message != null && message.ClientId.IsPresent())
            {
                var client = await clientStore.FindClientByIdAsync(message.ClientId);
                if (client != null)
                {
                    return client.EnableLocalLogin;
                }
            }

            return true;
        }


        private IHttpActionResult RenderLogoutPromptPage()
        {
            var logoutModel = new LogoutViewModel
            {
                SiteName = options.SiteName,
                SiteUrl = context.GetIdentityServerBaseUrl(),
                CurrentUser = context.GetCurrentUserDisplayName(),
                LogoutUrl = context.GetIdentityServerLogoutUrl(),
                AntiForgery = antiForgeryToken.GetAntiForgeryToken(),
            };

            return new LogoutActionResult(viewService, logoutModel);
        }

        private async Task<IHttpActionResult> RenderLoggedOutPage(string id)
        {
            Logger.Info("rendering logged out page");

            var baseUrl = context.GetIdentityServerBaseUrl();
            var iframeUrls = options.RenderProtocolUrls(baseUrl);

            var message = signOutMessageCookie.Read(id);
            var redirectUrl = message != null ? message.ReturnUrl : null;
            var clientName = await clientStore.GetClientName(message);

            var loggedOutModel = new LoggedOutViewModel
            {
                SiteName = options.SiteName,
                SiteUrl = baseUrl,
                IFrameUrls = iframeUrls,
                ClientName = clientName,
                RedirectUrl = redirectUrl,
                AutoRedirect = options.AuthenticationOptions.EnablePostSignOutAutoRedirect,
                AutoRedirectDelay = options.AuthenticationOptions.PostSignOutAutoRedirectDelay
            };
            return new LoggedOutActionResult(viewService, loggedOutModel);
        }

        private IHttpActionResult RenderErrorPage(string message = null)
        {
            message = message ?? localizationService.GetMessage(MessageIds.UnexpectedError);
            var errorModel = new ErrorViewModel
            {
                RequestId = context.GetRequestId(),
                SiteName = this.options.SiteName,
                SiteUrl = context.GetIdentityServerBaseUrl(),
                ErrorMessage = message,
                CurrentUser = context.GetCurrentUserDisplayName(),
                LogoutUrl = context.GetIdentityServerLogoutUrl(),
            };
            var errorResult = new ErrorActionResult(viewService, errorModel);
            return errorResult;
        }

        IHttpActionResult AuthorizeError(string error, SignInMessage message, IdentityServerOptions _options)
        {
            Logger.Info("In AuthorizeError");
            var authQuery = new Uri(message.ReturnUrl);
            var redirectUri = authQuery.ParseQueryString()["redirect_uri"];
            var state = authQuery.ParseQueryString()["state"];
            var responseMode = authQuery.ParseQueryString()["response_mode"] ?? "";

            Logger.InfoFormat("authQuery: {0}", authQuery);
            Logger.InfoFormat("redirectUri: {0}", redirectUri);
            Logger.InfoFormat("state: {0}", state);
            Logger.InfoFormat("responseMode: {0}", responseMode);

            //RaiseFailureEvent(error);
            var request = new ValidatedAuthorizeRequest
            {
                Options = _options,
                Subject = Principal.Anonymous,
                ResponseMode = responseMode
            };

            // return error to client
            var response = new AuthorizeResponse
            {
                Request = request,

                IsError = true,
                Error = error,
                State = state,
                RedirectUri = redirectUri
            };

            if (responseMode == Constants.ResponseModes.FormPost)
            {
                return new AuthorizeFormPostResult(response, Request);
            }
            else
            {
                return new AuthorizeRedirectResult(response, _options);
            }
        }
    }
}