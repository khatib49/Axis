using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Individual accounts in the Chart of Accounts
    /// Supports hierarchical structure (parent-child relationships)
    /// </summary>
    [Table("accounts")]
    public class Account
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("account_number")]
        [MaxLength(20)]
        public string AccountNumber { get; set; } = string.Empty;

        [Required]
        [Column("account_name")]
        [MaxLength(200)]
        public string AccountName { get; set; } = string.Empty;

        [Required]
        [Column("account_type_id")]
        public int AccountTypeId { get; set; }

        /// <summary>
        /// For hierarchical accounts (e.g., 1000 is parent of 1100, 1200)
        /// Null = top-level account
        /// </summary>
        [Column("parent_account_id")]
        public int? ParentAccountId { get; set; }

        [Column("description")]
        [MaxLength(1000)]
        public string? Description { get; set; }

        /// <summary>
        /// Current balance of the account
        /// Calculated field, updated by triggers or application logic
        /// </summary>
        [Column("current_balance")]
        [Precision(18, 2)]
        public decimal CurrentBalance { get; set; } = 0;

        [Required]
        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// System accounts cannot be deleted (Cash, Revenue, etc.)
        /// </summary>
        [Required]
        [Column("is_system_account")]
        public bool IsSystemAccount { get; set; } = false;

        /// <summary>
        /// Can transactions be posted directly to this account?
        /// False for header accounts that only have children
        /// </summary>
        [Required]
        [Column("allow_manual_entry")]
        public bool AllowManualEntry { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Column("created_by")]
        public int? CreatedBy { get; set; }

        [Column("modified_at")]
        public DateTime? ModifiedAt { get; set; }

        [Column("modified_by")]
        public int? ModifiedBy { get; set; }

        // Navigation properties
        [ForeignKey("AccountTypeId")]
        public virtual AccountType AccountType { get; set; } = default!;

        [ForeignKey("ParentAccountId")]
        public virtual Account? ParentAccount { get; set; }

        public virtual ICollection<Account> ChildAccounts { get; set; } = new List<Account>();

        public virtual ICollection<JournalEntryLine> JournalEntryLines { get; set; } = new List<JournalEntryLine>();
    }
}