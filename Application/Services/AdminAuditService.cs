using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class AdminAuditService : IAdminAuditService
    {
        private readonly IBaseRepository<AdminAuditLog> _repo;

        public AdminAuditService(IBaseRepository<AdminAuditLog> repo) => _repo = repo;

        public async Task<PaginatedResponse<AdminAuditLogDto>> ListAsync(AdminAuditFilterDto filter, CancellationToken ct = default)
        {
            var q = _repo.Query().AsQueryable();

            if (!string.IsNullOrWhiteSpace(filter.EntityType))
                q = q.Where(x => x.EntityType == filter.EntityType);
            if (!string.IsNullOrWhiteSpace(filter.Action))
                q = q.Where(x => x.Action == filter.Action);
            if (!string.IsNullOrWhiteSpace(filter.ChangedBy))
            {
                var needle = filter.ChangedBy.ToLower();
                q = q.Where(x => x.ChangedBy != null && x.ChangedBy.ToLower().Contains(needle));
            }
            if (filter.EntityId.HasValue)
                q = q.Where(x => x.EntityId == filter.EntityId.Value);
            if (filter.From.HasValue)
                q = q.Where(x => x.ChangedOn >= filter.From.Value);
            if (filter.To.HasValue)
            {
                var toExclusive = filter.To.Value.Date.AddDays(1);
                q = q.Where(x => x.ChangedOn < toExclusive);
            }

            var totalCount = await q.CountAsync(ct);
            var page = filter.Page < 1 ? 1 : filter.Page;
            var pageSize = filter.PageSize < 1 ? 50 : filter.PageSize;

            var rows = await q
                .OrderByDescending(x => x.ChangedOn).ThenByDescending(x => x.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new AdminAuditLogDto(
                    x.Id, x.EntityType, x.EntityId, x.EntityName,
                    x.Action, x.FieldChanges, x.Snapshot,
                    x.ChangedBy, x.ChangedOn))
                .ToListAsync(ct);

            return new PaginatedResponse<AdminAuditLogDto>(totalCount, rows, page, pageSize);
        }

        public async Task<IReadOnlyList<string>> DistinctEntityTypesAsync(CancellationToken ct = default)
        {
            return await _repo.Query()
                .Select(x => x.EntityType)
                .Distinct()
                .OrderBy(s => s)
                .ToListAsync(ct);
        }
    }
}
