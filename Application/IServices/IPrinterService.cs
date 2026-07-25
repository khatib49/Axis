using Application.DTOs;

namespace Application.IServices
{
    public interface IPrinterService
    {
        Task<PrinterDto?> GetAsync(int id, CancellationToken ct = default);
        Task<List<PrinterDto>> ListAsync(PrinterListFilterDto filter, CancellationToken ct = default);
        Task<PrinterDto> CreateAsync(PrinterCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, PrinterUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>Sends a small test ticket to the printer via the on-site agent. Returns false if the printer id is unknown.</summary>
        Task<bool> TestPrintAsync(int id, CancellationToken ct = default);
    }
}
