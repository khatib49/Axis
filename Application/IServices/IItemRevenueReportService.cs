using Application.DTOs;

namespace Application.IServices
{
    public interface IItemRevenueReportService
    {
        Task<ItemRevenueReportDto> GetReportAsync(
            ItemRevenueReportRequestDto request,
            CancellationToken ct = default);
    }
}
