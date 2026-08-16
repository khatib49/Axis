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
        private readonly IBaseRepository<SettingItem> _repoSettingItem;
        private readonly IBaseRepository<Item> _repoItem;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;
        public SettingService(
            IBaseRepository<Setting> repo,
            IBaseRepository<SettingItem> repoSettingItem,
            IBaseRepository<Item> repoItem,
            IUnitOfWork uow,
            DomainMapper mapper)
        {
            _repo = repo; _repoSettingItem = repoSettingItem; _repoItem = repoItem;
            _uow = uow; _mapper = mapper;
        }

        public async Task<SettingDto?> GetAsync(int id, CancellationToken ct = default)
        {
            // Active-only by default. Pass includeHidden=true via the controller
            // route to fetch a soft-deleted row (needed so the edit modal can
            // load and restore a hidden setting).
            var e = await _repo.Query()
                    .Include(s => s.Game)
                    .Include(s => s.Items).ThenInclude(si => si.Item).ThenInclude(i => i.Category)
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
                // Bundled items travel with the setting so the cashier screen
                // can show "includes 3x Booster" without a second round-trip.
                .Include(s => s.Items).ThenInclude(si => si.Item).ThenInclude(i => i.Category)
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
            // Mapperly can't build the child rows (the DTO carries ItemId +
            // quantity, not entities), so clear whatever it produced and
            // rebuild deliberately below.
            e.Items = new List<SettingItem>();
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);

            await SyncEventItemsAsync(e, dto.IsEvent, dto.Items, ct);
            await _uow.SaveChangesAsync(ct);

            return await GetAsync(e.Id, ct) ?? _mapper.ToDto(e);
        }
        public async Task<bool> UpdateAsync(int id, SettingUpdateDto dto, string? ModifiedBy, CancellationToken ct = default)
        {
            // Allow editing hidden rows too — that's how admins restore (the UI
            // only loads hidden rows when "Show hidden" is on, so a regular
            // edit can't accidentally touch one). If the dto explicitly carries
            // IsActive, honor it; otherwise leave the current flag alone.
            var e = await _repo.Query(asNoTracking: false)
                .Include(s => s.Items)
                .FirstOrDefaultAsync(s => s.Id == id, ct);
            if (e is null) return false;
            e.ModifiedBy = ModifiedBy ?? "";
            e.ModifiedOn = DateTime.UtcNow;

            // Snapshot the child rows: Mapperly would otherwise try to map the
            // DTO's Items (upsert shape) onto the entity's Items (full rows).
            var incomingItems = dto.Items;
            var incomingIsEvent = dto.IsEvent;

            _mapper.MapTo(dto, e); // updates only non-null fields
            if (dto.IsActive.HasValue)
                e.IsActive = dto.IsActive.Value;

            await SyncEventItemsAsync(e, incomingIsEvent, incomingItems, ct);

            await _uow.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>
        /// Makes the setting's bundled items match what the admin submitted.
        ///
        /// Rules:
        ///   • not an event, or no list sent → every bundled item is removed
        ///   • an item id that doesn't exist, or a quantity &lt;= 0 → skipped
        ///   • duplicate item ids collapse to the last one (the table has a
        ///     unique constraint on (SettingId, ItemId))
        ///
        /// Existing rows are updated in place rather than deleted and
        /// recreated, so ids stay stable.
        /// </summary>
        private async Task SyncEventItemsAsync(
            Setting setting, bool isEvent, List<SettingItemUpsertDto>? incoming, CancellationToken ct)
        {
            setting.IsEvent = isEvent;

            var existing = await _repoSettingItem.Query(asNoTracking: false)
                .Where(si => si.SettingId == setting.Id)
                .ToListAsync(ct);

            // Turning off the event flag drops the bundle — keeping orphaned
            // rows around would silently hand out stock if it were re-enabled.
            if (!isEvent || incoming is null || incoming.Count == 0)
            {
                if (existing.Count > 0) _repoSettingItem.RemoveRange(existing);
                return;
            }

            // Last write wins per item id, and drop anything meaningless.
            var wanted = new Dictionary<int, decimal>();
            foreach (var line in incoming)
            {
                if (line.ItemId <= 0 || line.QuantityPerPerson <= 0) continue;
                wanted[line.ItemId] = line.QuantityPerPerson;
            }

            if (wanted.Count == 0)
            {
                if (existing.Count > 0) _repoSettingItem.RemoveRange(existing);
                return;
            }

            // Reject item ids that don't exist rather than letting the FK blow
            // up mid-save with an opaque error.
            var validIds = await _repoItem.Query()
                .Where(i => wanted.Keys.Contains(i.Id))
                .Select(i => i.Id)
                .ToListAsync(ct);

            var missing = wanted.Keys.Except(validIds).ToList();
            if (missing.Count > 0)
                throw new ArgumentException($"Unknown item id(s): {string.Join(", ", missing)}.");

            foreach (var row in existing)
            {
                if (wanted.TryGetValue(row.ItemId, out var qty))
                {
                    row.QuantityPerPerson = qty;
                    wanted.Remove(row.ItemId);      // handled
                }
                else
                {
                    _repoSettingItem.Remove(row);   // no longer wanted
                }
            }

            foreach (var (itemId, qty) in wanted)
            {
                await _repoSettingItem.AddAsync(new SettingItem
                {
                    SettingId = setting.Id,
                    ItemId = itemId,
                    QuantityPerPerson = qty,
                    CreatedOn = DateTime.UtcNow,
                }, ct);
            }
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
