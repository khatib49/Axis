using System;
using System.ComponentModel.DataAnnotations.Schema;
using Domain.Identity;

namespace Domain.Entities
{
    [Table("transactions")]
    public class TransactionRecord
    {
        public Guid Id { get; set; }
        public string Reference { get; set; } = default!;
        public string Type { get; set; } = default!;
        public decimal Price { get; set; }

        public Guid UserId { get; set; }
        public AppUser User { get; set; } = default!;

        public Guid CardId { get; set; }
        public Card Card { get; set; } = default!;
        public Guid StatusId { get; set; }
        public Status Status { get; set; } = default!;
    }
}
