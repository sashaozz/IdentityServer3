﻿using BrockAllen.MembershipReboot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Thinktecture.IdentityServer.Admin.Core;
using Thinktecture.IdentityServer.Core;


namespace MembershipReboot.IdentityServer.Admin
{
    public class MembershipRebootUserManager : IUserManager, IDisposable
    {
        UserAccountService userAccountService;
        IUserAccountQuery query;
        IDisposable cleanup;

        public MembershipRebootUserManager(
            UserAccountService userAccountService, 
            IUserAccountQuery query,
            IDisposable cleanup)
        {
            if (userAccountService == null) throw new ArgumentNullException("userAccountService");
            if (query == null) throw new ArgumentNullException("query");

            this.userAccountService = userAccountService;
            this.query = query;
            this.cleanup = cleanup;
        }

        public void Dispose()
        {
            if (this.cleanup != null)
            {
                cleanup.Dispose();
                cleanup = null;
            }
        }

        public Task<UserManagerResult> CreateAsync(string username, IEnumerable<System.Security.Claims.Claim> claims)
        {
            return Task.FromResult(UserManagerResult.Success);
        }



        public Task<UserManagerResult<QueryResult>> QueryAsync(string filter, int start, int count)
        {
            int total;
            var users = query.Query(filter, start, count, out total);
            
            var result = new QueryResult();
            result.Start = start;
            result.Count = count;
            result.Total = total;
            result.Users = users.Select(x =>{
                var s = new UserSummary { 
                    Subject = x.ID.ToString("D"), 
                    Username = x.Username
                };
                if (!String.IsNullOrWhiteSpace(x.Email))
                {
                    s.Claims = new Claim[] { new Claim(Constants.ClaimTypes.Email, x.Email) };
                }
                return s;
            });
            
            return Task.FromResult(new UserManagerResult<QueryResult>(result));
        }
    }
}
