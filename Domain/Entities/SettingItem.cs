using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// An item bundled with an event game setting — the prerelease kit that
    /// comes with "Pre Release", the three boosters that come with "Draft".
    ///
    /// Starting a session on that setting hands these out: they are deducted
    /// from stock at <see cref="QuantityPerPerson"/> x the session's headcount,
    /// and appear on the invoice as included lines worth nothing. The event
    /// setting's own price is what the customer pays.
    /// </summary>
    [Table("SettingItems")]
    public class SettingItem
    {
        [Key] public int Id { get; set; }

        public int SettingId { get; set; }
        public Setting Setting { get; set; } = default!;

        public int ItemId { get; set; }
        public Item Item { get; set; } = default!;

        /// <summary>
        /// How many of this item each participant receives. Multiplied by the
        /// session's number of persons. Numeric rather than int so fractional
        /// bundles stay possible.
        /// </summary>
        [Column(TypeName = "numeric(18,3)")]
        public decimal QuantityPerPerson { get; set; } = 1;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
