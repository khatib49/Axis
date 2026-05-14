using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Journal Entry header - represents a single accounting transaction
    /// Each entry must have balanced debits and credits
    /// </summary>
    [Table("journal_entries")]
    public class JournalEntry
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        /// <summary>
        /// Auto-generated entry number (e.g., "JE-2024-00001")
        /// Format: JE-{YEAR}-{SEQUENCE}
        /// </summary>
        [Required]
        [Column("entry_number")]
        [MaxLength(50)]
        public string EntryNumber { get; set; } = string.Empty;

        [Required]
        [Column("entry_date")]
        public DateTime EntryDate { get; set; }

        [Required]
        [Column("description")]
        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// What triggered this journal entry:
        /// - "Transaction" (from TransactionRecord)
        /// - "Expense" (from Expense)
        /// - "Adjustment" (manual entry)
        /// - "Depreciation" (automated)
        /// - "Opening" (opening balances)
        /// </summary>
        [Required]
        [Column("reference_type")]
        [MaxLength(50)]
        public string ReferenceType { get; set; } = string.Empty;

        /// <summary>
        /// Foreign key to the source record (TransactionRecord.Id, Expense.Id, etc.)
        /// Null for manual adjustments
        /// </summary>
        [Column("reference_id")]
        public int? ReferenceId { get; set; }

        /// <summary>
        /// Total debit amount (must equal total credit amount)
        /// Denormalized for quick querying
        /// </summary>
        [Required]
        [Column("total_amount")]
        [Precision(18, 2)]
        public decimal TotalAmount { get; set; }

        /// <summary>
        /// Has this entry been posted to the ledger?
        /// Unposted entries can be edited, posted entries are locked
        /// </summary>
        [Required]
        [Column("is_posted")]
        public bool IsPosted { get; set; } = false;

        [Column("posted_at")]
        public DateTime? PostedAt { get; set; }

        [Column("posted_by")]
        public int? PostedBy { get; set; }

        /// <summary>
        /// Was this entry reversed/voided?
        /// </summary>
        [Required]
        [Column("is_voided")]
        public bool IsVoided { get; set; } = false;

        [Column("voided_at")]
        public DateTime? VoidedAt { get; set; }

        [Column("voided_by")]
        public int? VoidedBy { get; set; }

        [Column("void_reason")]
        [MaxLength(500)]
        public string? VoidReason { get; set; }

        [Required]
        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("created_by")]
        public int? CreatedBy { get; set; }

        [Column("modified_at")]
        public DateTime? ModifiedAt { get; set; }

        [Column("modified_by")]
        public int? ModifiedBy { get; set; }

        // Navigation property
        public virtual ICollection<JournalEntryLine> Lines { get; set; } = new List<JournalEntryLine>();
    }
}