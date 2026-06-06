using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class SettingService : ISettingService
    {
        private readonly IBaseRepository<Setting> _repo;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;
        public SettingService(IBaseRepository<Setting> repo, IUnitOfWork uow, DomainMapper mapper)
        {
            _repo = repo; _uow = uow; _mapper = mapper;
        }

        public async Task<SettingDto?> GetAsync(int id, CancellationToken ct = default)
        {
            // Active-only by default. Pass includeHidden=true via the controller
            // route to fetch a soft-deleted row (needed so the edit modal can
            // load and restore a hidden setting).
            var e = await _repo.Query()
                    .Include(s => s.Game)
                    .Where(s => s.IsActive)
                    .FirstOrDefaultAsync(s => s.Id == id, ct);
            return e is null ? null : _mapper.ToDto(e);
        }

        public async Task<PaginatedResponse<SettingDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default)
            => await ListAsync(pagination, includeHidden: false, ct);

        public async Task<PaginatedResponse<SettingDto>> ListAsync(BasePaginationRequestDto pagination, bool includeHidden, CancellationToken ct = default)
        {
            // Paginate in the database, not in memory — the Settings table has grown
            // large so materialising every row before paging was wasteful.
            var baseQ = _repo.Query();
            if (!includeHidden)
                baseQ = baseQ.Where(s => s.IsActive);

            var totalCount = await baseQ.CountAsync(ct);

            var pagedList = await baseQ
                .Include(s => s.Game)
                .OrderBy(s => s.Id)
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToListAsync(ct);

            var result = pagedList.Select(_mapper.ToDto).ToList();

            return new PaginatedResponse<SettingDto>(totalCount, result, pagination.Page, pagination.PageSize);
        }

        public async Task<SettingDto> CreateAsync(SettingCreateDto dto, string createdBy, CancellationToken ct = default)
        {
            var e = _mapper.ToEntity(dto);
            e.CreatedBy = createdBy ?? "";
            e.CreatedOn = DateTime.UtcNow;
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return _mapper.ToDto(e);
        }
        public async Task<bool> UpdateAsync(int id, SettingUpdateDto dto, string? ModifiedBy, CancellationToken ct = default)
        {
            // Allow editing hidden rows too — that's how admins restore (the UI
            // only loads hidden rows when "Show hidden" is on, so a regular
            // edit can't accidentally touch one). If the dto explicitly carries
            // IsActive, honor it; otherwise leave the current flag alone.
            var e = await _repo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(s => s.Id == id, ct);
            if (e is null) return false;
            e.ModifiedBy = ModifiedBy ?? "";
            e.ModifiedOn = DateTime.UtcNow;
            _mapper.MapTo(dto, e); // updates only non-null fields
            if (dto.IsActive.HasValue)
                e.IsActive = dto.IsActive.Value;
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            // Soft-delete: flip IsActive=false instead of removing. Hard-delete used
            // to fail in prod because TransactionRecord.GameSettingId / GameSession
            // FKs still reference old settings; this hides them from the UI without
            // breaking those joins.
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null || !e.IsActive) return false;

            e.IsActive = false;
            e.ModifiedOn = DateTime.UtcNow;
            _repo.Update(e);
            await _uow.SaveChangesAsync(ct);
            return true;
        }
    }
}
