namespace Domain.Entities
{
    public class Set
    {
        public int Id { get; set; }
        public int RoomId { get; set; }
        public Room Room { get; set; } = default!;

        // Examples: "A", "B", "C" or "Table 1", "Table 2"
        public string Name { get; set; } = default!;
        public ICollection<TransactionRecord> Transactions { get; set; } = new List<TransactionRecord>();

    }
}
