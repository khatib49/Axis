using Application.DTOs;

namespace Application.IServices
{
    public interface IStockBatchService
    {
        Task<PaginatedResponse<StockBatchDto>> ListAsync(StockBatchFilterDto filter, CancellationToken ct = default);
        Task<StockBatchDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<StockBatchDto>> ReceiveAsync(StockBatchCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<StockBatchDto>> UpdateAsync(int id, StockBatchUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<List<StockBatchDto>> GetExpiringAsync(int days = 7, CancellationToken ct = default);
        Task<List<StockBatchDto>> GetExpiredAsync(CancellationToken ct = default);
        Task<BaseResponse<StockBatchDto>> WriteOffExpiredAsync(int id, string? notes, CancellationToken ct = default);
    }
}
