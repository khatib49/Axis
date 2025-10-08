using System;
using Domain.Identity;

namespace Domain.Entities
{
    public class Receipt
    {
        public int Id { get; set; }

        public int TransactionId { get; set; }
        public TransactionRecord Transaction { get; set; } = default!;

        public int UserId { get; set; }
        public AppUser User { get; set; } = default!;

        public int CardId { get; set; }
        public Card Card { get; set; } = default!;

        public DateTime GeneratedOn { get; set; } = DateTime.UtcNow;
        public string Content { get; set; } = default!;
    }
}
