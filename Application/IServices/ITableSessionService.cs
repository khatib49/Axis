using Application.DTOs;

namespace Application.IServices
{
    /// <summary>Open / seat / transfer / merge / close table sessions. One open session per table.</summary>
    public interface ITableSessionService
    {
        Task<List<TableSessionDto>> ListOpenAsync(CancellationToken ct = default);
        Task<TableSessionDto?> GetAsync(int sessionId, CancellationToken ct = default);
        Task<TableSessionDto?> GetByTransactionAsync(int transactionId, CancellationToken ct = default);

        /// <summary>Opens an F&amp;B open invoice (status 7) and links it to the table.</summary>
        Task<BaseResponse<TableSessionDto>> OpenTableAsync(OpenTableRequestDto dto, CancellationToken ct = default);
        /// <summary>Links an already-open F&amp;B invoice to a free table.</summary>
        Task<BaseResponse<TableSessionDto>> SeatInvoiceAsync(SeatInvoiceRequestDto dto, CancellationToken ct = default);
        /// <summary>Moves the session's invoice to another free table (old session → Transferred, new session → Open).</summary>
        Task<BaseResponse<TableSessionDto>> TransferAsync(int sessionId, TransferSessionRequestDto dto, CancellationToken ct = default);
        /// <summary>Moves every item line of the source invoice into the target invoice and zeroes/closes the source.</summary>
        Task<BaseResponse<MergeResultDto>> MergeAsync(MergeInvoicesRequestDto dto, CancellationToken ct = default);
        /// <summary>Marks the open session of an invoice Closed (call after CloseOpenInvoice).</summary>
        Task<BaseResponse<TableSessionDto>> CloseByTransactionAsync(int transactionId, CancellationToken ct = default);
    }
}
