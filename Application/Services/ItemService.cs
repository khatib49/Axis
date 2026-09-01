using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Application.Services
{
    public class ItemService : IItemService
    {
        private readonly IBaseRepository<Item> _repo;
        private readonly IBaseRepository<ItemAddOn> _repoAddOn;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;
        private readonly IImageStorageService _imageStorage;

        public ItemService(IBaseRepository<Item> repo, IBaseRepository<ItemAddOn> repoAddOn,
            IUnitOfWork uow, DomainMapper mapper, IImageStorageService imageStorage)
        {
            _repo = repo; _repoAddOn = repoAddOn; _uow = uow; _mapper = mapper;
            _imageStorage = imageStorage;
        }

        public async Task<ItemDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.Query()
                .Include(x => x.AddOns.OrderBy(a => a.SortOrder).ThenBy(a => a.Id))
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return e is null ? null : _mapper.ToDto(e);
        }

        /// <summary>All add-ons of one item, inactive included (admin editor).</summary>
        public async Task<List<ItemAddOnDto>> GetAddOnsAsync(int itemId, CancellationToken ct = default)
            => await _repoAddOn.Query()
                .Where(a => a.ItemId == itemId)
                .OrderBy(a => a.SortOrder).ThenBy(a => a.Id)
                .Select(a => new ItemAddOnDto(a.Id, a.Name, a.Price, a.IsActive, a.SortOrder))
                .ToListAsync(ct);

        /// <summary>
        /// Replace-all sync of an item's add-ons from the admin editor.
        /// Rows referenced by historical orders can't be hard-deleted (FK
        /// RESTRICT), so anything removed from the list is deactivated when
        /// deletion fails.
        /// </summary>
        public async Task<List<ItemAddOnDto>> SetAddOnsAsync(int itemId, List<ItemAddOnUpsertDto> incoming, CancellationToken ct = default)
        {
            var item = await _repo.GetByIdAsync(itemId, asNoTracking: true, ct)
                       ?? throw new KeyNotFoundException("Item not found.");

            var existing = await _repoAddOn.Query(asNoTracking: false)
                .Where(a => a.ItemId == itemId)
                .ToListAsync(ct);

            incoming ??= new List<ItemAddOnUpsertDto>();

            // Validate first — half-applied edits are worse than rejected ones.
            foreach (var dto in incoming)
            {
                if (string.IsNullOrWhiteSpace(dto.Name))
                    throw new ArgumentException("Every add-on needs a name.");
                if (dto.Price < 0)
                    throw new ArgumentException($"Add-on '{dto.Name}' has a negative price.");
            }

            var keptIds = new HashSet<int>(incoming.Where(i => i.Id is > 0).Select(i => i.Id!.Value));

            foreach (var row in existing)
            {
                if (keptIds.Contains(row.Id)) continue;
                // Deactivate instead of delete: historical order lines point
                // here, and the editor's intent is "stop offering it".
                row.IsActive = false;
            }

            var sort = 0;
            foreach (var dto in incoming)
            {
                sort++;
                var match = dto.Id is > 0 ? existing.FirstOrDefault(a => a.Id == dto.Id.Value) : null;
                if (match != null)
                {
                    match.Name = dto.Name.Trim();
                    match.Price = Math.Round(dto.Price, 2);
                    match.IsActive = dto.IsActive;
                    match.SortOrder = sort;
                }
                else
                {
                    await _repoAddOn.AddAsync(new ItemAddOn
                    {
                        ItemId = item.Id,
                        Name = dto.Name.Trim(),
                        Price = Math.Round(dto.Price, 2),
                        IsActive = dto.IsActive,
                        SortOrder = sort,
                        CreatedOn = DateTime.UtcNow,
                    }, ct);
                }
            }

            await _uow.SaveChangesAsync(ct);
            return await GetAddOnsAsync(itemId, ct);
        }
        public async Task<PaginatedResponse<ItemDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            var page = pagination.Page <= 0 ? 1 : pagination.Page;
            var pageSize = pagination.PageSize <= 0 ? 20 : pagination.PageSize;

            var query = _repo.QueryableAsync(null, asNoTracking: true);

            // Filters
            if (pagination.CategoryId.HasValue)
                query = query.Where(x => x.CategoryId == pagination.CategoryId.Value);

            query = query.Where(x => x.StatusId == 1);// Get Only enabled items
            if (!string.IsNullOrWhiteSpace(pagination.search))
            {
                var term = pagination.search.Trim();
                // Postgres case-insensitive LIKE
                query = query.Where(x => x.Name != null && EF.Functions.ILike(x.Name, $"%{term}%"));
                // If term can include % or _ and you need literal matching, escape them and use ESCAPE clause via raw SQL or custom util.
            }

            var totalCount = await query.CountAsync(ct);

            // Deterministic ordering for paging
            query = query.OrderBy(x => x.Id); // or .OrderByDescending(x => x.CreatedOn).ThenBy(x => x.Id);

            var items = await query
                // Add-ons travel with the list so the cashier's customize
                // sheet needs no extra round-trip per item.
                .Include(x => x.AddOns.Where(a => a.IsActive).OrderBy(a => a.SortOrder).ThenBy(a => a.Id))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            var result = items.Select(_mapper.ToDto).ToList();
            return new PaginatedResponse<ItemDto>(totalCount, result, page, pageSize);
        }
        public async Task<ItemDto> CreateAsync(ItemCreateDto dto, CancellationToken ct = default)
        {
            var e = _mapper.ToEntity(dto);
            if (dto.Image != null)
            {
                e.ImagePath = await _imageStorage.SaveImageAsync(dto.Image, ct);
            }
            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            return _mapper.ToDto(e);
        }

        public async Task<bool> UpdateAsync(int id, ItemUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            _mapper.MapTo(dto, e); // updates only non-null fields
            if (dto.Image != null)
            {
                e.ImagePath = await _imageStorage.SaveImageAsync(dto.Image, ct);
            }
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.GetByIdAsync(id, asNoTracking: false, ct);
            if (e is null) return false;

            _repo.Remove(e);
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<PaginatedResponse<ItemDto>> GetByCategoryIdAsync(int id, BasePaginationRequestDto pagination, CancellationToken ct = default)
        {
            var list = await _repo.ListAsync(i => i.CategoryId == id, asNoTracking: true, ct);
            var totalCount = list.Count();

            var pagedList = list
                .Skip((pagination.Page - 1) * pagination.PageSize)
                .Take(pagination.PageSize)
                .ToList();

            var result = pagedList.Select(_mapper.ToDto).ToList();

            return new PaginatedResponse<ItemDto>(totalCount, result, pagination.Page, pagination.PageSize);
        }
    }
}
