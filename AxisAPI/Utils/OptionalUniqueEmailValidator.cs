using System.ComponentModel.DataAnnotations;
using Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace AxisAPI.Utils
{
    /// <summary>
    /// Email is optional (walk-in clients are created at the counter with only
    /// a phone and a name), but when one is given it must be valid and unique.
    ///
    /// Identity's own RequireUniqueEmail also makes email mandatory, which made
    /// every phone-only "create client" fail with "Email '' is invalid".
    /// </summary>
    public sealed class OptionalUniqueEmailValidator : IUserValidator<AppUser>
    {
        private static readonly EmailAddressAttribute EmailFormat = new();

        public async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user)
        {
            var email = await manager.GetEmailAsync(user);
            if (string.IsNullOrWhiteSpace(email))
                return IdentityResult.Success;

            var describer = manager.ErrorDescriber;
            if (!EmailFormat.IsValid(email))
                return IdentityResult.Failed(describer.InvalidEmail(email));

            var owner = await manager.FindByEmailAsync(email);
            if (owner != null && owner.Id != user.Id)
                return IdentityResult.Failed(describer.DuplicateEmail(email));

            return IdentityResult.Success;
        }
    }
}
