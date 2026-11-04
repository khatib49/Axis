using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Manager-pinned suggestion: when the trigger item (or any item of the trigger category) is in the cart, suggest SuggestItemId.</summary>
    [Table("Upsell_Rules")]
    public class UpsellRule
    {
        [Key] public int Id { get; set; }
        public int? TriggerItemId { get; set; }
        public int? TriggerCategoryId { get; set; }
        public int SuggestItemId { get; set; }
        /// <summary>Optional pitch shown to the guest / cashier ("Add a croissant for $2?").</summary>
        [MaxLength(200)] public string? Message { get; set; }
        /// <summary>Higher shows first.</summary>
        public int Priority { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(256)] public string CreatedBy { get; set; } = "system";
    }

    /// <summary>Learned pair from paid invoices: guests who bought ItemId also bought SuggestItemId. Rebuilt nightly.</summary>
    [Table("Upsell_Pairs")]
    public class UpsellPair
    {
        [Key] public int Id { get; set; }
        public int ItemId { get; set; }
        public int SuggestItemId { get; set; }
        /// <summary>Invoices that contain both items.</summary>
        public int PairCount { get; set; }
        /// <summary>P(suggest | item), 0..1.</summary>
        [Column(TypeName = "numeric(9,4)")] public decimal Confidence { get; set; }
        /// <summary>Confidence divided by the suggest item's base rate; above 1 means a real association.</summary>
        [Column(TypeName = "numeric(9,4)")] public decimal Lift { get; set; }
        /// <summary>Short AI-written pitch, when the AI key is configured.</summary>
        [MaxLength(200)] public string? Pitch { get; set; }
        public DateTime ComputedOn { get; set; } = DateTime.UtcNow;
    }

    /// <summary>One suggestion shown or accepted, for the acceptance-rate report.</summary>
    [Table("Upsell_Events")]
    public class UpsellEvent
    {
        [Key] public long Id { get; set; }
        /// <summary>Till | Waiter | Shop | SelfOrder | Kiosk</summary>
        [Required][MaxLength(20)] public string Channel { get; set; } = UpsellChannels.Till;
        /// <summary>Shown | Accepted</summary>
        [Required][MaxLength(10)] public string Action { get; set; } = UpsellActions.Shown;
        public int SuggestItemId { get; set; }
        /// <summary>Rule | Learned</summary>
        [Required][MaxLength(10)] public string Source { get; set; } = "Learned";
        [Column(TypeName = "numeric(18,2)")] public decimal Price { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public int? BranchId { get; set; }
    }

    public static class UpsellChannels
    {
        public const string Till = "Till";
        public static readonly string[] All = { "Till", "Waiter", "Shop", "SelfOrder", "Kiosk" };
        public static readonly string[] Public = { "Shop", "SelfOrder", "Kiosk" };
    }

    public static class UpsellActions
    {
        public const string Shown = "Shown";
        public const string Accepted = "Accepted";
    }
}
