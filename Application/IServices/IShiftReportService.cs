using Application.DTOs;
using Domain.Entities;

namespace Application.IServices
{
    public interface IShiftReportService
    {
        /// <summary>Computes the X (live) or Z (closing) report for a shift from tenders, paid transactions, movements and voids.</summary>
        Task<ShiftReportDto> BuildAsync(CashierShift shift, string reportType, CancellationToken ct = default);
    }
}
