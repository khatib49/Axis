using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Generic admin-action audit row. Captures every Create / Update /
    /// Delete on the entities we care about, regardless of which service
    /// initiated the change. Written automatically by a SaveChanges
    /// interceptor so callers don't have to remember to log.
    /// </summary>
    [Table("AdminAuditLogs")]
    public class AdminAuditLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(60)]
        public string EntityType { get; set; } = default!;

        public int? EntityId { get; set; }

        [MaxLength(300)]
        public string? EntityName { get; set; }

        // 'Created' | 'Updated' | 'Deleted'
        [Required]
        [MaxLength(20)]
        public string Action { get; set; } = default!;

        // For Updated: JSON of { field: { old, new } } — only the fields
        // that actually changed.
        public string? FieldChanges { get; set; }

        // For Created / Deleted: JSON snapshot of the row at change time.
        public string? Snapshot { get; set; }

        [MaxLength(200)]
        public string? ChangedBy { get; set; }

        public DateTime ChangedOn { get; set; } = DateTime.UtcNow;
    }
}
