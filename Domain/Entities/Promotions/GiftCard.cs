using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// Prepaid stored-value card. Redeeming it is a PAYMENT on an open invoice
    /// (it lowers TotalPrice), not a discount. Balance is the fast read; the
    /// GiftCardTransaction ledger is the truth.
    /// </summary>
    [Table("Promo_GiftCards")]
    public class GiftCard
    {
        public int Id { get; set; }

        /// <summary>16 uppercase alphanumerics, generated, unique.</summary>
        [Required][MaxLength(16)] public string Code { get; set; } = default!;

        [Column(TypeName = "numeric(18,2)")] public decimal InitialAmount { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal Balance { get; set; }

        [Required][MaxLength(3)] public string CurrencyCode { get; set; } = "USD";

        public DateTime IssuedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresOn { get; set; }

        /// <summary>Free-text name / phone of the person the card was issued to.</summary>
        [MaxLength(200)] public string? IssuedTo { get; set; }

        /// <summary>Optional link to a client account (no FK, no navigation).</summary>
        public int? UserId { get; set; }

        /// <summary>Active | Depleted | Expired | Disabled</summary>
        [Required][MaxLength(20)] public string Status { get; set; } = GiftCardStatus.Active;

        [MaxLength(500)] public string? Notes { get; set; }

        [MaxLength(200)] public string CreatedBy { get; set; } = "system";

        public int? BranchId { get; set; }

        public ICollection<GiftCardTransaction> Transactions { get; set; } = new List<GiftCardTransaction>();
    }

    public static class GiftCardStatus
    {
        public const string Active = "Active";
        public const string Depleted = "Depleted";
        public const string Expired = "Expired";
        public const string Disabled = "Disabled";
    }

    /// <summary>One movement on a gift card. Amount is always positive; Type says the direction.</summary>
    [Table("Promo_GiftCardTransactions")]
    public class GiftCardTransaction
    {
        public int Id { get; set; }

        public int GiftCardId { get; set; }
        public GiftCard GiftCard { get; set; } = default!;

        /// <summary>Load | Redeem | Refund | Adjust</summary>
        [Required][MaxLength(20)] public string Type { get; set; } = default!;

        [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }

        /// <summary>The existing invoice (transactions.Id) a Redeem paid for / a Refund came from.</summary>
        public int? TransactionId { get; set; }

        [Column(TypeName = "numeric(18,2)")] public decimal BalanceAfter { get; set; }

        public DateTime On { get; set; } = DateTime.UtcNow;
        [MaxLength(200)] public string By { get; set; } = "system";
        [MaxLength(500)] public string? Note { get; set; }
    }

    public static class GiftCardTransactionType
    {
        public const string Load = "Load";
        public const string Redeem = "Redeem";
        public const string Refund = "Refund";
        public const string Adjust = "Adjust";
    }
}
