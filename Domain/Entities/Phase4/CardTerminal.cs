using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A card payment terminal at a register.</summary>
    [Table("Pay_CardTerminals")]
    public class CardTerminal
    {
        [Key] public int Id { get; set; }
        [Required][MaxLength(120)] public string Name { get; set; } = default!;
        /// <summary>Manual | CloudHttp | Simulator</summary>
        [Required][MaxLength(20)] public string Provider { get; set; } = CardTerminalProviders.Manual;
        /// <summary>The terminal's id at the provider / bank (TID).</summary>
        [MaxLength(80)] public string? ProviderTerminalId { get; set; }
        public int? BranchId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(256)] public string CreatedBy { get; set; } = "system";
    }

    /// <summary>One card payment pushed to a terminal against an open invoice.</summary>
    [Table("Pay_TerminalPayments")]
    public class TerminalPayment
    {
        [Key] public int Id { get; set; }
        public int TransactionId { get; set; }
        public int TerminalId { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }
        [Required][MaxLength(3)] public string Currency { get; set; } = "USD";
        /// <summary>Our reference sent to the provider; unique.</summary>
        [Required][MaxLength(80)] public string ExternalId { get; set; } = default!;
        /// <summary>Pending | Approved | Declined | Cancelled | Expired | Failed</summary>
        [Required][MaxLength(20)] public string Status { get; set; } = TerminalPaymentStatuses.Pending;
        [MaxLength(20)] public string? ApprovalCode { get; set; }
        [MaxLength(4)] public string? CardLast4 { get; set; }
        [MaxLength(20)] public string? CardScheme { get; set; }
        [MaxLength(40)] public string? Rrn { get; set; }
        [MaxLength(300)] public string? Message { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(256)] public string CreatedBy { get; set; } = "system";
        public DateTime? CompletedOn { get; set; }
        public DateTime? LastCheckedOn { get; set; }
        public string? Raw { get; set; }
        public int? BranchId { get; set; }
    }

    public static class CardTerminalProviders
    {
        /// <summary>Stand-alone terminal: the cashier types the approval code and last 4 digits.</summary>
        public const string Manual = "Manual";
        /// <summary>Provider / bank cloud API or a local bridge exposing the Axis terminal HTTP contract.</summary>
        public const string CloudHttp = "CloudHttp";
        /// <summary>Test only; requires CardTerminal.AllowSimulator = true.</summary>
        public const string Simulator = "Simulator";
        public static readonly string[] All = { Manual, CloudHttp, Simulator };
    }

    public static class TerminalPaymentStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Declined = "Declined";
        public const string Cancelled = "Cancelled";
        public const string Expired = "Expired";
        public const string Failed = "Failed";
    }
}
