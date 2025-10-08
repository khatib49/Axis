namespace Domain.Entities
{
    public class TransactionItem
    {
        
        public int TransactionRecordId { get; set; }
        public TransactionRecord TransactionRecord { get; set; } = default!;

        public int ItemId { get; set; }
        public Item Item { get; set; } = default!;

      
        public int Quantity { get; set; }
        
    }
}
