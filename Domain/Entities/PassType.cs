using System;
using System.Collections.Generic;

namespace Domain.Entities
{
    public class PassType
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public int DurationInMinutes { get; set; }
        public decimal Price { get; set; }

        public Guid GameId { get; set; }
        public Game Game { get; set; } = default!;

        public ICollection<GameSession> GameSessions { get; set; } = new List<GameSession>();
    }
}
