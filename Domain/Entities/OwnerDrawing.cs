using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Cash taken out of the business by an owner for personal use.
    /// NOT an expense: it reduces equity, never profit. Each row posts
    ///     DEBIT  owner's Drawings account (33xx, Equity)
    ///     CREDIT 1000 Cash on Hand
    /// as journal entry ReferenceType "OwnerDrawing".
    /// </summary>
    [Table("OwnerDrawings")]
    public class OwnerDrawing
    {
        [Key]
        public int Id { get; set; }

        public int OwnerId { get; set; }
        public Owner Owner { get; set; } = default!;

        [Precision(18, 2)]
        public decimal Amount { get; set; }

        public DateTime DrawingDate { get; set; }

        [MaxLength(50)]
        public string? PaymentMethod { get; set; }

        [MaxLength(500)]
        public string? Comment { get; set; }

        /// <summary>The posted journal entry. Null only if posting failed.</summary>
        public int? JournalEntryId { get; set; }

        // Cancelled drawings stay on file for audit; their journal entry is
        // voided so the ledger no longer counts them.
        public bool IsVoided { get; set; }
        public DateTime? VoidedOn { get; set; }
        public int? VoidedBy { get; set; }

        [MaxLength(500)]
        public string? VoidReason { get; set; }

        public int? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
    }
}
