using System;
using Domain.Identity;

namespace Domain.Entities
{
    public class GameSession
    {
        public int Id { get; set; }

        public int UserId { get; set; }
        public AppUser User { get; set; } = default!;

        public int CardId { get; set; }
        public Card Card { get; set; } = default!;

        public int GameId { get; set; }
        public Game Game { get; set; } = default!;

        public int RoomId { get; set; }
        public Room Room { get; set; } = default!;

        public int PassTypeId { get; set; }
        public PassType PassType { get; set; } = default!;

        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public DateTime? EndTime { get; set; }
        public bool IsOpenTime { get; set; }    // true if open-ended
        public string Status { get; set; } = "Active";
    }
}
