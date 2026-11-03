using Application.DTOs;

namespace Application.IServices
{
    public interface IFixedAssetService
    {
        Task<List<FixedAssetDto>> ListAsync(string? status, CancellationToken ct = default);
        Task<BaseResponse<FixedAssetDto>> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<FixedAssetDto>> CreateAsync(FixedAssetUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<FixedAssetDto>> UpdateAsync(int id, FixedAssetUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse> DeleteAsync(int id, CancellationToken ct = default);

        Task<BaseResponse<List<DepreciationScheduleRowDto>>> GetScheduleAsync(int id, CancellationToken ct = default);
        Task<List<DepreciationEntryDto>> GetEntriesAsync(int id, CancellationToken ct = default);

        /// <summary>Posts one depreciation journal entry per active asset for the month. Idempotent per asset/month.</summary>
        Task<BaseResponse<DepreciationRunResultDto>> RunDepreciationAsync(int year, int month, int? assetId, CancellationToken ct = default);

        Task<BaseResponse<FixedAssetDto>> DisposeAsync(int id, AssetDisposeDto dto, CancellationToken ct = default);

        Task<AssetRegisterSummaryDto> GetRegisterSummaryAsync(CancellationToken ct = default);
    }
}
