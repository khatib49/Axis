using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A real-world bank account mapped to a GL (asset) account.</summary>
    [Table("Acct_BankAccounts")]
    public class BankAccount
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = default!;

        [MaxLength(200)]
        public string? BankName { get; set; }

        [MaxLength(50)]
        public string? AccountNumberMasked { get; set; }

        [MaxLength(3)]
        public string CurrencyCode { get; set; } = "USD";

        /// <summary>Existing Account (chart of accounts) id this bank account posts to.</summary>
        public int GlAccountId { get; set; }

        public bool IsActive { get; set; } = true;

        public int? BranchId { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
