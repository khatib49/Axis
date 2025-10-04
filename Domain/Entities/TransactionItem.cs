namespace Domain.Entities
{
    public class TransactionItem
    {
        
        public Guid TransactionRecordId { get; set; }
        public TransactionRecord TransactionRecord { get; set; } = default!;

        public Guid ItemId { get; set; }
        public Item Item { get; set; } = default!;

      
        public int Quantity { get; set; }
        
    }
}
