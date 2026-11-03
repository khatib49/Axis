using Application.DTOs;

namespace Application.IServices
{
    /// <summary>Builds report payloads from existing data and renders them to Html / Csv.</summary>
    public interface IBiReportService
    {
        /// <param name="reportType">DailySales | WeeklySales | LowStock | OpenInvoices | ShiftSummary</param>
        /// <param name="day">Business day (UTC midnight) the report is about; ignored by point-in-time reports.</param>
        Task<ReportPayload> BuildAsync(string reportType, DateTime day, int? branchId, CancellationToken ct = default);
        string Render(ReportPayload payload, string format);
    }
}
