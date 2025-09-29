namespace Domain.Entities
{
    public class Game
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid CategoryId { get; set; }
        public Category? Category { get; set; } = default!;
        public Guid StatusId { get; set; }       
        public Status Status { get; set; } = default!;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }

        public ICollection<PassType> PassTypes { get; set; } = new List<PassType>();
        public ICollection<GameSession> GameSessions { get; set; } = new List<GameSession>();
        public ICollection<Setting> Settings { get; set; } = new List<Setting>();
        public ICollection<Item> Items { get; set; } = new List<Item>();

        public ICollection<TransactionRecord> Transactions { get; set; } = new List<TransactionRecord>();

    }
}
