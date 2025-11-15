using Domain.Entities;
using Microsoft.AspNetCore.Identity;

namespace Domain.Identity
{
    public class AppUser : IdentityUser<int>
    {
        public string? DisplayName { get; set; }

        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public int StatusId { get; set; }
        public Status Status { get; set; } = default!;
        public DateTimeOffset? DeletedAt { get; set; }
    }

    public enum UserStatus
    {
        Active = 1,
        Disabled = 2,
        Deleted = 3,
        Suspended = 8
    }
}
