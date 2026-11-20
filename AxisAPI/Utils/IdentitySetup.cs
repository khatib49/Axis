using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace AxisAPI.Utils
{
    /// <summary>Identity configuration shared by Program.cs and the tests.</summary>
    public static class IdentitySetup
    {
        public static IdentityBuilder AddAxisIdentity(IServiceCollection services)
        {
            // Emailed password-reset links (PasswordResetService) expire after 2h.
            services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
            return services
                .AddIdentityCore<AppUser>(o =>
                {
                    o.Password.RequiredLength = 8;
                    o.Password.RequireNonAlphanumeric = false;
                    // Uniqueness is enforced by OptionalUniqueEmailValidator,
                    // which (unlike this flag) allows users with no email.
                    o.User.RequireUniqueEmail = false;
                })
                .AddUserValidator<OptionalUniqueEmailValidator>()
                .AddRoles<AppRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders();
        }
    }
}
