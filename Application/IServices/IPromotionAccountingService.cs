using Application.DTOs;

namespace Application.IServices
{
    /// <summary>
    /// Phase 3: ledger side of the Promotions module. Every method posts ONE
    /// balanced journal entry through IJournalService, keyed by
    /// (ReferenceType, ReferenceId, Sequence) in Promo_AccountingPostings so a
    /// repeat never books twice. A failure (locked period, missing account,
    /// journal error) is returned as Success=false with the posting left
    /// Pending; callers log it and surface it but never roll back the business
    /// action. RepostPendingAsync retries pending rows later.
    /// </summary>
    public interface IPromotionAccountingService
    {
        /// <summary>Creates 2150 Gift Card Liability and 4950 Promotional Discounts once (idempotent) and resolves cash / sales accounts.</summary>
        Task<BaseResponse<PromotionAccountsDto>> EnsureAccountsAsync(CancellationToken ct = default);

        /// <summary>DR 1000 Cash / CR 2150 Gift Card Liability. ReferenceType GiftCardLoad, ReferenceId = gift card transaction id.</summary>
        Task<BaseResponse<PromotionPostingResultDto>> OnGiftCardIssuedOrLoadedAsync(int giftCardId, int giftCardTransactionId, decimal amount, CancellationToken ct = default);

        /// <summary>DR 2150 Gift Card Liability / CR 4100 sales revenue. ReferenceType GiftCardRedeem.</summary>
        Task<BaseResponse<PromotionPostingResultDto>> OnGiftCardRedeemedAsync(int giftCardId, int giftCardTransactionId, decimal amount, int invoiceId, CancellationToken ct = default);

        /// <summary>Reverse of the redeem: DR 4100 sales revenue / CR 2150 Gift Card Liability (value put back on the card is owed again; no cash moves). ReferenceType GiftCardRefund.</summary>
        Task<BaseResponse<PromotionPostingResultDto>> OnGiftCardRefundedAsync(int giftCardId, int giftCardTransactionId, decimal amount, int? invoiceId, CancellationToken ct = default);

        /// <summary>
        /// kind = Coupon | HappyHour | Combo. Negative delta (discount applied): DR 4950 / CR 4100 by |delta|;
        /// positive delta (discount refunded): the reverse. ReferenceType Promotion{kind}, ReferenceId = the
        /// application / redemption row id, Sequence = its version.
        /// </summary>
        Task<BaseResponse<PromotionPostingResultDto>> OnPromotionDiscountAsync(string kind, int referenceId, int sequence, int invoiceId, decimal delta, CancellationToken ct = default);

        Task<List<PromotionPostingDto>> GetPostingsAsync(DateTime? from, DateTime? to, string? status, CancellationToken ct = default);
        Task<List<PromotionPostingDto>> GetPendingAsync(CancellationToken ct = default);
        Task<BaseResponse<PromotionRepostResultDto>> RepostPendingAsync(CancellationToken ct = default);
        /// <summary>Posts gift card movements / promotion applications recorded before accounting existed (no posting row yet).</summary>
        Task<BaseResponse<PromotionRepostResultDto>> BackfillAsync(CancellationToken ct = default);

        Task<BaseResponse<GiftCardLiabilityReportDto>> GetGiftCardLiabilityReportAsync(CancellationToken ct = default);
        Task<BaseResponse<PromotionDiscountsReportDto>> GetDiscountsReportAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
    }
}
