using Application.DTOs;

namespace Application.IServices
{
    public interface IGiftCardService
    {
        Task<BaseResponse<GiftCardDto>> IssueAsync(GiftCardIssueDto dto, CancellationToken ct = default);
        Task<PaginatedResponse<GiftCardDto>> ListAsync(GiftCardFilterDto filter, CancellationToken ct = default);
        Task<GiftCardDto?> GetAsync(int id, CancellationToken ct = default);
        Task<List<GiftCardTransactionDto>> GetTransactionsAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<GiftCardDto>> DisableAsync(int id, string? note, CancellationToken ct = default);
        Task<BaseResponse<GiftCardDto>> AdjustAsync(int id, GiftCardAdjustDto dto, CancellationToken ct = default);
        Task<BaseResponse<GiftCardDto>> LoadAsync(int id, GiftCardLoadDto dto, CancellationToken ct = default);
        Task<BaseResponse<GiftCardDto>> RefundAsync(int id, GiftCardRefundDto dto, CancellationToken ct = default);

        /// <summary>Till: balance + can-redeem check by code.</summary>
        Task<BaseResponse<GiftCardLookupDto>> LookupAsync(string code, CancellationToken ct = default);
        /// <summary>Till: apply part of the balance as payment on an open invoice.</summary>
        Task<BaseResponse<GiftCardRedeemResultDto>> RedeemAsync(GiftCardRedeemDto dto, CancellationToken ct = default);
    }
}
