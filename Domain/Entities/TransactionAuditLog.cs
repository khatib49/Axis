using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("TransactionAuditLogs")]
    public class TransactionAuditLog
    {
        public int Id { get; set; }
        public int TransactionId { get; set; }
        public TransactionRecord Transaction { get; set; } = default!;

        public string ChangedBy { get; set; } = default!;      // username from JWT
        public DateTime ChangedOn { get; set; } = DateTime.UtcNow;
        public string Action { get; set; } = default!;          // e.g. "StatusUpdate", "PriceAdjust"

        // Snapshot of what changed (JSON or plain fields)
        public string? FieldChanged { get; set; }               // e.g. "StatusId"
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public string? Notes { get; set; }                      // free-text reason/comment
    }
}