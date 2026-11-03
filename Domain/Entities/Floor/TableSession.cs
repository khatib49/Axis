using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Links an open F&amp;B invoice (TransactionRecord, status 7) to a dining
    /// table. At most one Open session per table at a time.
    /// </summary>
    [Table("Floor_TableSessions")]
    public class TableSession
    {
        public const string StatusOpen = "Open";
        public const string StatusClosed = "Closed";
        public const string StatusTransferred = "Transferred";
        public const string StatusMerged = "Merged";

        public int Id { get; set; }
        public int TableId { get; set; }
        public DiningTable Table { get; set; } = default!;
        /// <summary>Existing TransactionRecord id (no navigation on the transaction side).</summary>
        public int TransactionId { get; set; }
        public int GuestCount { get; set; } = 1;
        public DateTime OpenedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ClosedOn { get; set; }
        [Required][MaxLength(256)] public string OpenedBy { get; set; } = default!;
        /// <summary>Open | Closed | Transferred | Merged.</summary>
        [MaxLength(20)] public string Status { get; set; } = StatusOpen;
        [MaxLength(500)] public string? Notes { get; set; }
        public int? BranchId { get; set; }
    }
}
