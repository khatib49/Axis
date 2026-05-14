using Application.DTOs;

namespace Application.IServices
{

    public interface IAccountingReportService
    {
        Task<AccountingDashboardDto> GetDashboardAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
        Task<List<ExpenseCategoryLineDto>> GetExpensesBreakdownAsync(DateTime? from, DateTime? to, bool capitalOnly, CancellationToken ct = default);
    }
}
