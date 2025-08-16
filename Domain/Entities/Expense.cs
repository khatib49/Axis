using System;

namespace Domain.Entities
{
    public class Expense
    {
        public Guid Id { get; set; }
        public string Category { get; set; } = default!;
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
