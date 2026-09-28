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
        private readonly IBaseRepository<ItemVariant> _repoVariant;
        private readonly IUnitOfWork _uow;
        private readonly DomainMapper _mapper;
        private readonly IImageStorageService _imageStorage;

        public ItemService(IBaseRepository<Item> repo, IBaseRepository<ItemAddOn> repoAddOn,
            IUnitOfWork uow, DomainMapper mapper, IImageStorageService imageStorage, IBaseRepository<ItemVariant> repoVariant)
        {
            _repo = repo; _repoAddOn = repoAddOn; _uow = uow; _mapper = mapper; _repoVariant = repoVariant;
            _imageStorage = imageStorage;
        }

        public async Task<ItemDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.Query()
                .Include(x => x.AddOns.OrderBy(a => a.SortOrder).ThenBy(a => a.Id))
                .Include(x => x.Variants.OrderBy(v => v.SortOrder).ThenBy(v => v.Id))
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

        // ── Variants (colour / type with own stock) ─────────────────────
        public async Task<List<ItemVariantDto>> GetVariantsAsync(int itemId, CancellationToken ct = default)
            => await _repoVariant.Query()
                .Where(v => v.ItemId == itemId)
                .OrderBy(v => v.SortOrder).ThenBy(v => v.Id)
                .Select(v => new ItemVariantDto(v.Id, v.Name, v.Color, v.Sku, v.PriceDelta, v.Quantity, v.IsActive, v.SortOrder))
                .ToListAsync(ct);

        /// <summary>
        /// Replace-all sync of an item's variants. Removed rows are
        /// deactivated (historical lines reference them). After the sync the
        /// item's own Quantity is set to the sum of its ACTIVE variants so
        /// every existing stock screen and low-stock check keeps working.
        /// </summary>
        public async Task<List<ItemVariantDto>> SetVariantsAsync(int itemId, List<ItemVariantUpsertDto> incoming, CancellationToken ct = default)
        {
            var item = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(i => i.Id == itemId, ct)
                       ?? throw new KeyNotFoundException("Item not found.");
            var existing = await _repoVariant.Query(asNoTracking: false).Where(v => v.ItemId == itemId).ToListAsync(ct);
            incoming ??= new List<ItemVariantUpsertDto>();

            foreach (var dto in incoming)
            {
                if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Every option needs a name.");
                if (dto.Quantity < 0) throw new ArgumentException($"Option '{dto.Name}' has a negative stock.");
            }
            var names = incoming.Select(i => i.Name.Trim().ToLowerInvariant()).ToList();
            if (names.Count != names.Distinct().Count()) throw new ArgumentException("Two options have the same name.");

            var keptIds = new HashSet<int>(incoming.Where(i => i.Id is > 0).Select(i => i.Id!.Value));
            foreach (var row in existing)
                if (!keptIds.Contains(row.Id)) row.IsActive = false;

            var sort = 0;
            foreach (var dto in incoming)
            {
                sort++;
                var match = dto.Id is > 0 ? existing.FirstOrDefault(v => v.Id == dto.Id.Value) : null;
                if (match != null)
                {
                    match.Name = dto.Name.Trim();
                    match.Color = string.IsNullOrWhiteSpace(dto.Color) ? null : dto.Color.Trim();
                    match.Sku = string.IsNullOrWhiteSpace(dto.Sku) ? null : dto.Sku.Trim();
                    match.PriceDelta = Math.Round(dto.PriceDelta, 2);
                    match.Quantity = dto.Quantity;
                    match.IsActive = dto.IsActive;
                    match.SortOrder = sort;
                }
                else
                {
                    var v = new ItemVariant
                    {
                        ItemId = item.Id, Name = dto.Name.Trim(),
                        Color = string.IsNullOrWhiteSpace(dto.Color) ? null : dto.Color.Trim(),
                        Sku = string.IsNullOrWhiteSpace(dto.Sku) ? null : dto.Sku.Trim(),
                        PriceDelta = Math.Round(dto.PriceDelta, 2), Quantity = dto.Quantity,
                        IsActive = dto.IsActive, SortOrder = sort, CreatedOn = DateTime.UtcNow,
                    };
                    await _repoVariant.AddAsync(v, ct);
                    existing.Add(v);
                }
            }

            // Item stock mirrors the options.
            if (existing.Any(v => v.IsActive))
                item.Quantity = existing.Where(v => v.IsActive).Sum(v => v.Quantity);

            await _uow.SaveChangesAsync(ct);
            return await GetVariantsAsync(itemId, ct);
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
                .Include(x => x.Variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder).ThenBy(v => v.Id))
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
