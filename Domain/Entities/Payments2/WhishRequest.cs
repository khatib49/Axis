using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// One Whish Collect request raised against an existing open invoice
    /// (TransactionRecord). The collect URL doubles as the QR payload shown
    /// on the customer display / phone. Verified server-side before Paid.
    /// </summary>
    [Table("Pay_WhishRequests")]
    public class WhishRequest
    {
        [Key] public int Id { get; set; }
        /// <summary>Existing TransactionRecord id (open F&amp;B invoice).</summary>
        public int TransactionId { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }
        [Required][MaxLength(3)] public string Currency { get; set; } = "USD";
        /// <summary>Our reference sent to Whish as externalId; echoed back and used for the status check.</summary>
        [Required][MaxLength(80)] public string ExternalId { get; set; } = default!;
        [MaxLength(1000)] public string? CollectUrl { get; set; }
        /// <summary>Pending | Paid | Failed | Expired | Cancelled</summary>
        [Required][MaxLength(20)] public string Status { get; set; } = WhishRequestStatuses.Pending;
        /// <summary>Till | SelfOrder | Kiosk | Display</summary>
        [Required][MaxLength(20)] public string Source { get; set; } = WhishRequestSources.Till;
        /// <summary>UI URL the buyer is bounced to after the return endpoint verified the payment.</summary>
        [MaxLength(1000)] public string? ReturnUrl { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(256)] public string CreatedBy { get; set; } = "system";
        public DateTime? PaidOn { get; set; }
        public DateTime? LastCheckedOn { get; set; }
        /// <summary>Raw provider response captured when the request reached a terminal state.</summary>
        public string? RawCallback { get; set; }
        public int? BranchId { get; set; }
    }

    public static class WhishRequestStatuses
    {
        public const string Pending = "Pending";
        public const string Paid = "Paid";
        public const string Failed = "Failed";
        public const string Expired = "Expired";
        public const string Cancelled = "Cancelled";
        public static bool IsTerminal(string s) => s != Pending;
    }

    public static class WhishRequestSources
    {
        public const string Till = "Till";
        public const string SelfOrder = "SelfOrder";
        public const string Kiosk = "Kiosk";
        public const string Display = "Display";
        public static readonly string[] All = { Till, SelfOrder, Kiosk, Display };
    }
}
