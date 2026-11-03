using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>One payment (in one currency, by one method) against a transaction.</summary>
    [Table("Till_PaymentTenders")]
    public class PaymentTender
    {
        public int Id { get; set; }
        /// <summary>Existing TransactionRecord id.</summary>
        public int TransactionId { get; set; }
        public int? ShiftId { get; set; }
        public string CurrencyCode { get; set; } = default!;
        [Column(TypeName = "numeric(18,2)")]
        public decimal AmountInCurrency { get; set; }
        [Column(TypeName = "numeric(18,6)")]
        public decimal RateUsed { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal AmountInBase { get; set; }
        /// <summary>"Cash" | "Card" | "Wallet" | "Transfer" | "GiftCard" | "Other".</summary>
        public string Method { get; set; } = TenderMethods.Cash;
        public string? Reference { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = default!;
        public int? BranchId { get; set; }
    }

    public static class TenderMethods
    {
        public const string Cash = "Cash";
        public static readonly string[] All = { "Cash", "Card", "Wallet", "Transfer", "GiftCard", "Other" };
    }
}
