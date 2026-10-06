using Application.DTOs;

namespace Application.IServices
{
    public interface IOwnerService
    {
        // Owners
        Task<IReadOnlyList<OwnerDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
        Task<OwnerDto?> GetAsync(int id, CancellationToken ct = default);
        Task<OwnerDto> CreateAsync(OwnerCreateDto dto, int? userId, CancellationToken ct = default);
        Task<OwnerDto> UpdateAsync(int id, OwnerUpdateDto dto, int? userId, CancellationToken ct = default);
        // Soft-hide. Drawings history and the account stay intact.
        Task<bool> DeactivateAsync(int id, CancellationToken ct = default);

        // Drawings
        Task<PagedOwnerDrawingsResult> QueryDrawingsAsync(OwnerDrawingFilter filter, CancellationToken ct = default);
        Task<OwnerDrawingDto> CreateDrawingAsync(OwnerDrawingCreateDto dto, int? userId, CancellationToken ct = default);
        Task<OwnerDrawingDto> UpdateDrawingAsync(int id, OwnerDrawingUpdateDto dto, int? userId, CancellationToken ct = default);
        Task<OwnerDrawingDto> VoidDrawingAsync(int id, string? reason, int? userId, CancellationToken ct = default);

        // Report
        Task<OwnerDrawingsSummaryDto> GetSummaryAsync(DateTime? from, DateTime? to, CancellationToken ct = default);

        /// <summary>Cash paid out as drawings through this page, all time (non-voided).</summary>
        Task<decimal> GetLifetimeDrawingsCashOutAsync(CancellationToken ct = default);
    }
}
