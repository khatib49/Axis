using System;
using Domain.Identity;

namespace Domain.Entities
{
    public class Receipt
    {
        public Guid Id { get; set; }

        public Guid TransactionId { get; set; }
        public TransactionRecord Transaction { get; set; } = default!;

        public Guid UserId { get; set; }
        public AppUser User { get; set; } = default!;

        public Guid CardId { get; set; }
        public Card Card { get; set; } = default!;

        public DateTime GeneratedOn { get; set; } = DateTime.UtcNow;
        public string Content { get; set; } = default!;
    }
}
