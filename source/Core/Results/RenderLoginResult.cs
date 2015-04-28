/*
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

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Policy;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Owin;
using Thinktecture.IdentityServer.Core.Configuration;
using Thinktecture.IdentityServer.Core.Configuration.Hosting;
using Thinktecture.IdentityServer.Core.Extensions;
using Thinktecture.IdentityServer.Core.Logging;
using Thinktecture.IdentityServer.Core.Models;
using Thinktecture.IdentityServer.Core.ViewModels;

namespace Thinktecture.IdentityServer.Core.Results
{
    /// <summary>
    /// 
    /// </summary>
    //public class RenderLoginResult : IHttpActionResult
    //{
    //    private readonly static ILog Logger = LogProvider.GetCurrentClassLogger();

    //    private readonly IOwinContext context;
    //    private readonly IdentityServerOptions options;
    //    private LastUserNameCookie lastUserNameCookie;
    //    private readonly SignInMessage message;
    //    private readonly string signInMessageId;
    //    private readonly string errorMessage;
    //    private string username;
    //    private readonly bool rememberMe;

    //    /// <summary>
    //    /// 
    //    /// </summary>        
    //    /// <param name="lastUserNameCookie"></param>
    //    /// <param name="message"></param>
    //    /// <param name="signInMessageId"></param>
    //    /// <param name="errorMessage"></param>
    //    /// <param name="username"></param>
    //    /// <param name="rememberMe"></param>
    //    /// <exception cref="ArgumentNullException"></exception>
    //    public RenderLoginResult(IOwinContext context,
    //        IdentityServerOptions options,
    //        LastUserNameCookie lastUserNameCookie,
    //        SignInMessage message, string signInMessageId, string errorMessage = null, string username = null, bool rememberMe = false)
    //    {
    //        this.context = context;
    //        this.options = options;
    //        this.lastUserNameCookie = lastUserNameCookie;
    //        this.message = message;
    //        this.signInMessageId = signInMessageId;
    //        this.errorMessage = errorMessage;
    //        this.username = username;
    //        this.rememberMe = rememberMe;
    //    }

    //    public Task<HttpResponseMessage> ExecuteAsync(CancellationToken cancellationToken)
    //    {
    //        return Task.FromResult(Execute());
    //    }
    //}

}