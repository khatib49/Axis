using Application.DTOs;

namespace Application.IServices
{
    /// <summary>QR self-ordering from a table (public endpoints) + table admin.</summary>
    public interface ISelfOrderService
    {
        // ── Admin ────────────────────────────────────────────────────────
        Task<List<SelfOrderTableDto>> ListTablesAsync(CancellationToken ct = default);
        Task<BaseResponse<SelfOrderTableDto>> CreateTableAsync(SelfOrderTableUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<SelfOrderTableDto>> UpdateTableAsync(int id, SelfOrderTableUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteTableAsync(int id, CancellationToken ct = default);
        /// <summary>Public URL + QR payload per table. publicBaseUrl: dashboard origin (falls back to setting SelfOrder.PublicBaseUrl).</summary>
        Task<List<SelfOrderTableQrDto>> ListTableQrsAsync(string? publicBaseUrl, CancellationToken ct = default);

        // ── Public ───────────────────────────────────────────────────────
        Task<BaseResponse<SelfOrderMenuDto>> GetMenuAsync(string tableCode, CancellationToken ct = default);
        Task<BaseResponse<SelfOrderSessionStartedDto>> StartSessionAsync(string tableCode, CancellationToken ct = default);
        Task<BaseResponse<SelfOrderSessionStatusDto>> SubmitAsync(Guid token, SelfOrderSubmitDto dto, CancellationToken ct = default);
        Task<BaseResponse<SelfOrderSessionStatusDto>> GetStatusAsync(Guid token, CancellationToken ct = default);
    }
}
