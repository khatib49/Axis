using System;
using Domain.Identity;

namespace Domain.Entities
{
    public class Notification
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public AppUser User { get; set; } = default!;

        public string Title { get; set; } = default!;
        public string Body { get; set; } = default!;
        public string Type { get; set; } = default!;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public bool IsRead { get; set; }
    }
}
