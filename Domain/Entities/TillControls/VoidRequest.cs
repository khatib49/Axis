using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A cashier's request to void or refund an existing transaction.</summary>
    [Table("Till_VoidRequests")]
    public class VoidRequest
    {
        public int Id { get; set; }
        /// <summary>Existing TransactionRecord id.</summary>
        public int TransactionId { get; set; }
        /// <summary>"Void" | "Refund".</summary>
        public string Type { get; set; } = default!;
        [Column(TypeName = "numeric(18,2)")]
        public decimal? Amount { get; set; }
        public int ReasonId { get; set; }
        public VoidReason Reason { get; set; } = default!;
        public string? ReasonText { get; set; }
        public string RequestedBy { get; set; } = default!;
        public DateTime RequestedOn { get; set; } = DateTime.UtcNow;
        /// <summary>"Pending" | "Approved" | "Rejected".</summary>
        public string Status { get; set; } = VoidRequestStatuses.Pending;
        public string? DecidedBy { get; set; }
        public DateTime? DecidedOn { get; set; }
        public string? DecisionNote { get; set; }
        public int? BranchId { get; set; }
    }

    public static class VoidRequestTypes
    {
        public const string Void = "Void";
        public const string Refund = "Refund";
    }

    public static class VoidRequestStatuses
    {
        public const string Pending = "Pending";
        public const string Approved = "Approved";
        public const string Rejected = "Rejected";
    }
}
