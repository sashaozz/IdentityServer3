using System;
using System.Linq;
using Thinktecture.IdentityServer.Core.Models;

namespace Thinktecture.IdentityServer.Core.Extensions
{
    internal static class SignInMessageExtensions
    {
        public static bool TryGetAcrValue(this SignInMessage message, string key, out string value)
        {
            value = null;

            if (message.AcrValues == null || !message.AcrValues.Any())
            {
                return false;
            }

            key += ":";

            var acrValue = message.AcrValues.FirstOrDefault(v => v.StartsWith(key));

            if (String.IsNullOrWhiteSpace(acrValue))
            {
                return false;
            }

            value = acrValue.Remove(0, key.Length);

            return true;
        }
    }
}
