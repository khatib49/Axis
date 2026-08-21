using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Domain.Identity;

namespace Domain.Entities
{
    /// <summary>
    /// A client's prepaid balance, spendable on anything — games or food.
    ///
    /// <see cref="Balance"/> is the fast read; <see cref="Transactions"/> is
    /// the truth. Every mutation goes through WalletService, which writes a
    /// ledger row AND a journal entry in the same operation, so the wallet,
    /// the ledger and account 2100 can never drift apart silently.
    /// </summary>
    [Table("Wallets")]
    public class Wallet
    {
        [Key] public int Id { get; set; }

        /// <summary>One wallet per person — unique index on the column.</summary>
        public int UserId { get; set; }
        public AppUser User { get; set; } = default!;

        /// <summary>Never negative; DB CHECK constraint backs this up.</summary>
        [Column(TypeName = "numeric(18,2)")]
        public decimal Balance { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }

        public ICollection<WalletTransaction> Transactions { get; set; } = new List<WalletTransaction>();
    }

    /// <summary>One movement on a wallet. Amount is always positive; Type says the direction.</summary>
    [Table("WalletTransactions")]
    public class WalletTransaction
    {
        [Key] public int Id { get; set; }

        public int WalletId { get; set; }
        public Wallet Wallet { get; set; } = default!;

        /// <summary>TopUp | Bonus | Spend | Refund | Adjustment.</summary>
        [Required][MaxLength(20)] public string Type { get; set; } = default!;

        [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }

        /// <summary>Snapshot after this movement — makes the history readable at a glance.</summary>
        [Column(TypeName = "numeric(18,2)")] public decimal BalanceAfter { get; set; }

        /// <summary>How top-up money arrived (Cash / Whish / Card). Null for spends.</summary>
        [MaxLength(20)] public string? Method { get; set; }

        /// <summary>The sale a Spend paid for. Null otherwise.</summary>
        public int? TransactionRecordId { get; set; }
        public TransactionRecord? TransactionRecord { get; set; }

        [MaxLength(500)] public string? Notes { get; set; }

        [MaxLength(200)] public string CreatedBy { get; set; } = "";
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// "Top up at least MinAmount → get +BonusPercent extra credit."
    /// The highest MinAmount that the top-up reaches wins; tiers don't stack.
    /// </summary>
    [Table("WalletBonusTiers")]
    public class WalletBonusTier
    {
        [Key] public int Id { get; set; }

        [Column(TypeName = "numeric(18,2)")] public decimal MinAmount { get; set; }

        [Column(TypeName = "numeric(5,2)")] public decimal BonusPercent { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
