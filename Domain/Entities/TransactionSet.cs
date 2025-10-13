namespace Domain.Entities
{
    public class TransactionSet
    {
        public int TransactionRecordId { get; set; }
        public TransactionRecord TransactionRecord { get; set; } = default!;

        public int RoomSetId { get; set; }
        public Set Set  { get; set; } = default!;

    }
}
