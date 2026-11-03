using Application.DTOs;

namespace Application.IServices
{
    public interface IReceivablesService
    {
        /// <summary>AR aging over open F&amp;B invoices (TransactionRecord.StatusId == 7), aged by CreatedOn.</summary>
        Task<ArAgingReportDto> GetArAgingAsync(DateTime? asOf, CancellationToken ct = default);
    }
}
