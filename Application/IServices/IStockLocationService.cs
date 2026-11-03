using Application.DTOs;

namespace Application.IServices
{
    public interface IStockLocationService
    {
        Task<List<StockLocationDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
        Task<StockLocationDto?> GetAsync(int id, CancellationToken ct = default);
        Task<StockLocationDto> GetOrCreateDefaultAsync(CancellationToken ct = default);
        Task<BaseResponse<StockLocationDto>> CreateAsync(StockLocationUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<StockLocationDto>> UpdateAsync(int id, StockLocationUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeactivateAsync(int id, CancellationToken ct = default);
        Task<List<LocationStockDto>> GetStockAsync(int locationId, string? search, CancellationToken ct = default);
    }
}
