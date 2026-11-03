using Application.DTOs;

namespace Application.IServices
{
    public interface IStockCountService
    {
        Task<PaginatedResponse<StockCountSummaryDto>> ListAsync(string? status, int? locationId, int page = 1, int pageSize = 25, CancellationToken ct = default);
        Task<StockCountDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<StockCountDto>> CreateDraftAsync(StockCountCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<StockCountDto>> UpdateLinesAsync(int id, StockCountLinesUpdateDto dto, CancellationToken ct = default);
        Task<BaseResponse<StockCountDto>> PostAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<bool>> CancelAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<StockCountVarianceReportDto>> VarianceReportAsync(int id, int top = 10, CancellationToken ct = default);
    }
}
