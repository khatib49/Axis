using Application.DTOs;

namespace Application.IServices
{
    public interface IHappyHourService
    {
        Task<List<HappyHourRuleDto>> ListAsync(bool includeInactive = true, CancellationToken ct = default);
        Task<HappyHourRuleDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<HappyHourRuleDto>> CreateAsync(HappyHourRuleUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<HappyHourRuleDto>> UpdateAsync(int id, HappyHourRuleUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>Rules in effect at <paramref name="atUtc"/> (default: now), evaluated in venue local time.</summary>
        Task<HappyHourActiveNowDto> ActiveNowAsync(DateTime? atUtc = null, CancellationToken ct = default);
        Task<BaseResponse<HappyHourPricePreviewDto>> PricePreviewAsync(int itemId, DateTime? atUtc = null, CancellationToken ct = default);
        /// <summary>Discounts the matching lines of an open invoice once (idempotent per invoice).</summary>
        Task<BaseResponse<HappyHourApplyResultDto>> ApplyAsync(HappyHourApplyDto dto, CancellationToken ct = default);
    }
}
