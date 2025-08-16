using System;
using Domain.Identity;

namespace Domain.Entities
{
    public class Room
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public bool IsAvailable { get; set; } = true;

        public Guid GameId { get; set; }
        public Game Game { get; set; } = default!;

        public Guid? AssignedUserId { get; set; }
        public AppUser? AssignedUser { get; set; }

        public DateTime? CurrentSessionStartTime { get; set; }
    }
}
