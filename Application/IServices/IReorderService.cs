using Application.DTOs;

namespace Application.IServices
{
    public interface IReorderService
    {
        Task<List<ReorderSuggestionDto>> GetSuggestionsAsync(CancellationToken ct = default);
        Task<List<ReorderRuleDto>> ListRulesAsync(CancellationToken ct = default);
        Task<ReorderRuleDto?> GetRuleAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<ReorderRuleDto>> CreateRuleAsync(ReorderRuleUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<ReorderRuleDto>> UpdateRuleAsync(int id, ReorderRuleUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteRuleAsync(int id, CancellationToken ct = default);
        /// <summary>
        /// Builds a PurchaseCreateDto from the current suggestions. Nothing is
        /// saved: the UI hands the payload to the existing purchases page.
        /// </summary>
        Task<BaseResponse<PurchaseCreateDto>> BuildPurchaseDraftAsync(ReorderPurchaseDraftRequestDto request, CancellationToken ct = default);
    }
}
