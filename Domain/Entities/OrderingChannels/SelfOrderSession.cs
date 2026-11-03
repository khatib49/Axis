using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>One guest's ordering session from a QR table. Token is the bearer the phone holds.</summary>
    [Table("SelfOrder_Sessions")]
    public class SelfOrderSession
    {
        [Key] public int Id { get; set; }
        [Required][MaxLength(40)] public string TableCode { get; set; } = default!;
        public Guid Token { get; set; } = Guid.NewGuid();
        /// <summary>Open F&B invoice created on first submit (null until then).</summary>
        public int? TransactionId { get; set; }
        /// <summary>Open | Submitted | Closed</summary>
        [Required][MaxLength(20)] public string Status { get; set; } = SelfOrderSessionStatuses.Open;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityOn { get; set; } = DateTime.UtcNow;
        [MaxLength(500)] public string? GuestNote { get; set; }
        public int? BranchId { get; set; }
    }

    public static class SelfOrderSessionStatuses
    {
        public const string Open = "Open";
        public const string Submitted = "Submitted";
        public const string Closed = "Closed";
    }
}
