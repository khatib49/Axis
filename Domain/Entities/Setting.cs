namespace Domain.Entities
{
    public class Setting
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        public string Type { get; set; } = default!;

        public decimal Hours { get; set; } = 0;
        public decimal Price { get; set; } = 0;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; } = null;
        public string CreatedBy { get; set; } = default!;
        public string? ModifiedBy { get; set; } = null;

        public int GameId { get; set; }
        public Game Game { get; set; } = default!;
        public bool IsOffer { get; set; } = false;
        public bool IsOpenHour { get; set; } = false;
        public bool IsDayPass { get; set; } = false;

        /// <summary>
        /// Marks this setting as an EVENT (Pre Release, Draft, tournament…).
        /// An event can bundle stock items via <see cref="Items"/>: starting a
        /// session hands them out and deducts them from stock, while the
        /// customer pays only this setting's Price.
        /// </summary>
        public bool IsEvent { get; set; } = false;

        /// <summary>Items handed out with this event. Empty for normal settings.</summary>
        public ICollection<SettingItem> Items { get; set; } = new List<SettingItem>();

        // Soft-hide flag. Backed by a manually added DB column (one-off ALTER):
        //   ALTER TABLE "Settings" ADD COLUMN "IsActive" boolean NOT NULL DEFAULT true;
        // Default true keeps existing rows visible. The Delete endpoint flips this
        // to false instead of removing the row, so referenced GameSettings stay
        // intact for historical TransactionRecord/GameSession joins.
        public bool IsActive { get; set; } = true;

    }
}
