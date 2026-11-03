using Application.DTOs;

namespace Application.IServices
{
    public interface IStockTransferService
    {
        Task<PaginatedResponse<StockTransferDto>> ListAsync(string? status, int? locationId, int page = 1, int pageSize = 25, CancellationToken ct = default);
        Task<StockTransferDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<StockTransferDto>> CreateAsync(StockTransferCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<StockTransferDto>> UpdateAsync(int id, StockTransferUpdateDto dto, CancellationToken ct = default);
        Task<BaseResponse<StockTransferDto>> PostAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<bool>> CancelAsync(int id, CancellationToken ct = default);
    }
}
