using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// One accounting posting attempted by the Promotions module (phase 3).
    /// The (ReferenceType, ReferenceId, Sequence) key is unique, which is what
    /// makes every post once-only: a repeat with the same key returns the row
    /// already written instead of booking a second journal entry.
    ///
    /// JournalEntryId is null while the row is Pending (the accounting period
    /// was locked, the chart of accounts was incomplete, or the journal refused
    /// the entry); the business action it describes (gift card load / redeem /
    /// refund, promotion discount) has ALREADY happened and is never rolled
    /// back. POST api/promotions/accounting/repost retries pending rows.
    /// </summary>
    [Table("Promo_AccountingPostings")]
    public class PromotionAccountingPosting
    {
        public int Id { get; set; }

        /// <summary>GiftCardLoad | GiftCardRedeem | GiftCardRefund | PromotionCoupon | PromotionHappyHour | PromotionCombo</summary>
        [Required][MaxLength(40)] public string ReferenceType { get; set; } = default!;

        /// <summary>Gift card transaction id, coupon redemption id, happy hour / combo application id.</summary>
        public int ReferenceId { get; set; }

        /// <summary>1 for one-shot references; the application row Version for re-evaluated promotions.</summary>
        public int Sequence { get; set; } = 1;

        /// <summary>Signed amount as the caller gave it (gift card: positive money; promotion: delta, negative = discount applied).</summary>
        [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }

        /// <summary>The invoice (transactions.Id) the posting relates to, when any.</summary>
        public int? InvoiceId { get; set; }

        public int? GiftCardId { get; set; }

        /// <summary>Set once the journal entry is created and posted.</summary>
        public int? JournalEntryId { get; set; }

        /// <summary>Pending | Posted</summary>
        [Required][MaxLength(20)] public string Status { get; set; } = PromotionPostingStatus.Pending;

        /// <summary>Why the last attempt did not post (period locked, missing account, journal error).</summary>
        [MaxLength(500)] public string? Error { get; set; }

        [MaxLength(500)] public string? Description { get; set; }

        /// <summary>When the business action happened (the entry date used for the journal).</summary>
        public DateTime On { get; set; } = DateTime.UtcNow;

        public DateTime? PostedOn { get; set; }

        public int Attempts { get; set; }

        [MaxLength(200)] public string By { get; set; } = "system";

        public int? BranchId { get; set; }
    }

    public static class PromotionPostingStatus
    {
        public const string Pending = "Pending";
        public const string Posted = "Posted";
    }

    public static class PromotionPostingType
    {
        public const string GiftCardLoad = "GiftCardLoad";
        public const string GiftCardRedeem = "GiftCardRedeem";
        public const string GiftCardRefund = "GiftCardRefund";
        public const string PromotionCoupon = "PromotionCoupon";
        public const string PromotionHappyHour = "PromotionHappyHour";
        public const string PromotionCombo = "PromotionCombo";

        public static string ForPromotionKind(string kind) => "Promotion" + kind;
        public static bool IsPromotion(string type) => type.StartsWith("Promotion", StringComparison.Ordinal);
        public static string Kind(string type) => IsPromotion(type) ? type.Substring("Promotion".Length) : type;
    }
}
