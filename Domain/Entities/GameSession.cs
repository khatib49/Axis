using System;
using Domain.Identity;

namespace Domain.Entities
{
    public class GameSession
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }
        public AppUser User { get; set; } = default!;

        public Guid CardId { get; set; }
        public Card Card { get; set; } = default!;

        public Guid GameId { get; set; }
        public Game Game { get; set; } = default!;

        public Guid RoomId { get; set; }
        public Room Room { get; set; } = default!;

        public Guid PassTypeId { get; set; }
        public PassType PassType { get; set; } = default!;

        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public bool IsOpenTime { get; set; }    // true if open-ended
        public string Status { get; set; } = "Active";
    }
}
