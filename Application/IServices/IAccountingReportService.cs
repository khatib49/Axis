using Application.DTOs;

namespace Application.IServices
{

    public interface IAccountingReportService
    {
        Task<AccountingDashboardDto> GetDashboardAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
        Task<List<ExpenseCategoryLineDto>> GetExpensesBreakdownAsync(DateTime? from, DateTime? to, bool capitalOnly, CancellationToken ct = default);
        // Reconciliation between TransactionRecord.TotalPrice (the calculator)
        // and the chart of accounts revenue side. Surfaces orphan transactions
        // that have no journal entry, plus the live discrepancy figure.
        Task<RevenueCoverageAuditDto> GetRevenueCoverageAuditAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
    }
}
