using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A sales channel an order can come through — e.g. Toters, Online Web,
    /// Phone, Walk-In. Optional on TransactionRecord (null = direct in-house).
    /// Backed by a manually-created DB table; see the ALTER script delivered
    /// alongside the code change.
    /// </summary>
    [Table("Channels")]
    public class Channel
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = default!;

        [MaxLength(500)]
        public string? Description { get; set; }

        // Soft-delete flag. We never hard-delete a channel because historical
        // transactions point to it; flipping IsActive=false just hides it from
        // pickers in the UI.
        [Required]
        public bool IsActive { get; set; } = true;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }

        public ICollection<TransactionRecord> Transactions { get; set; } = new List<TransactionRecord>();
    }
}
