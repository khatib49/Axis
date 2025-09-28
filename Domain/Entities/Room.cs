namespace Domain.Entities
{
    public class Room
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid? CategoryId { get; set; }
        public Category? Category { get; set; }
        public int? Sets { get; set; }
        public ICollection<TransactionRecord> Transactions { get; set; } = new List<TransactionRecord>();

    }
}
