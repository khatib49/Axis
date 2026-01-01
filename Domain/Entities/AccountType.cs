using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Represents the main categories in the Chart of Accounts
    /// (Assets, Liabilities, Equity, Revenue, Expenses)
    /// </summary>
    [Table("account_types")]
    public class AccountType
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Required]
        [Column("type_name")]
        [MaxLength(50)]
        public string TypeName { get; set; } = string.Empty;

        /// <summary>
        /// Normal balance side: "Debit" or "Credit"
        /// Assets, Expenses = Debit
        /// Liabilities, Equity, Revenue = Credit
        /// </summary>
        [Required]
        [Column("normal_balance")]
        [MaxLength(10)]
        public string NormalBalance { get; set; } = string.Empty;

        [Column("display_order")]
        public int DisplayOrder { get; set; }

        [Column("description")]
        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        [Column("is_active")]
        public bool IsActive { get; set; } = true;

        [Column("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public virtual ICollection<Account> Accounts { get; set; } = new List<Account>();
    }
}