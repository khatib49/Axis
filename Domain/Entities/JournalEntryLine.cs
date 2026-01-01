using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Individual debit/credit lines within a Journal Entry
    /// Each Journal Entry must have at least 2 lines (debit and credit)
    /// Total debits must equal total credits
    /// </summary>
    [Table("journal_entry_lines")]
    public class JournalEntryLine
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("journal_entry_id")]
        public int JournalEntryId { get; set; }

        [Required]
        [Column("account_id")]
        public int AccountId { get; set; }

        /// <summary>
        /// Debit amount (asset increases, expense increases, liability/equity/revenue decreases)
        /// </summary>
        [Required]
        [Column("debit_amount")]
        [Precision(18, 2)]
        public decimal DebitAmount { get; set; } = 0;

        /// <summary>
        /// Credit amount (liability/equity/revenue increases, asset/expense decreases)
        /// </summary>
        [Required]
        [Column("credit_amount")]
        [Precision(18, 2)]
        public decimal CreditAmount { get; set; } = 0;

        [Column("description")]
        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// Order of lines within the journal entry (for display purposes)
        /// </summary>
        [Required]
        [Column("line_number")]
        public int LineNumber { get; set; }

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("JournalEntryId")]
        public virtual JournalEntry JournalEntry { get; set; } = default!;

        [ForeignKey("AccountId")]
        public virtual Account Account { get; set; } = default!;
    }
}