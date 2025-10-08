namespace Domain.Entities
{
    public class Room
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public int? CategoryId { get; set; }
        public Category? Category { get; set; }
        public int? Sets { get; set; }
        public ICollection<TransactionRecord> Transactions { get; set; } = new List<TransactionRecord>();

    }
}
