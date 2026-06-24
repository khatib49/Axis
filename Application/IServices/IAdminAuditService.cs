using Application.DTOs;

namespace Application.IServices
{
    public interface IAdminAuditService
    {
        Task<PaginatedResponse<AdminAuditLogDto>> ListAsync(AdminAuditFilterDto filter, CancellationToken ct = default);

        // Used by the FE to populate the EntityType dropdown — just the
        // distinct list of entity types that have logged rows.
        Task<IReadOnlyList<string>> DistinctEntityTypesAsync(CancellationToken ct = default);
    }
}
