namespace Domain.Entities
{
    public class Room
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public int? CategoryId { get; set; }
        public Category? Category { get; set; }
        public bool IsOpenSet { get; set; } = false;

        public ICollection<Set> Sets{ get; set; } = new List<Set>();
        public ICollection<TransactionRecord> Transactions { get; set; } = new List<TransactionRecord>();

    }
}
