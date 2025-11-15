namespace Domain.Entities
{
    public class Discount
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public string? Description { get; set; } = default!;
        public string Type { get; set; } = default!; // e.g., "Percentage", "FixedAmount" .
        public decimal Amount { get; set; }
        public bool IsActive { get; set; } = false;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;

    }
}
