using Microsoft.AspNetCore.Identity;

namespace Domain.Identity
{
    public class AppUser : IdentityUser<int>
    {
        public string? DisplayName { get; set; }
    }
}
