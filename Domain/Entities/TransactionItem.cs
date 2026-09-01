namespace Domain.Entities
{
    public class TransactionItem
    {

        public int TransactionRecordId { get; set; }
        public TransactionRecord TransactionRecord { get; set; } = default!;

        public int ItemId { get; set; }
        public Item Item { get; set; } = default!;


        public int Quantity { get; set; }

        /// <summary>
        /// True when this line came bundled with an event game setting (a
        /// prerelease kit, draft boosters…). It is deducted from stock and
        /// printed on the receipt so the customer sees what they received,
        /// but it contributes NOTHING to the total — the event price already
        /// covers it. Every price recompute must skip these lines.
        /// </summary>
        public bool IsIncluded { get; set; } = false;

        /// <summary>Paid extras chosen for this line (name/price snapshotted).</summary>
        public ICollection<TransactionItemAddOn> AddOns { get; set; } = new List<TransactionItemAddOn>();
    }
}
