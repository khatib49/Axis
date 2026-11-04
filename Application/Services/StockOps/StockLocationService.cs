using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.StockOps
{
    public class StockLocationService : IStockLocationService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<StockLocationService> _logger;

        public StockLocationService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<StockLocationService> logger)
        {
            _db = db; _user = user; _logger = logger;
        }

        public async Task<List<StockLocationDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
        {
            await StockOpsSupport.EnsureDefaultLocationAsync(_db, null, ct);
            var branchId = _user.BranchId;
            if (branchId.HasValue) await StockOpsSupport.GetDefaultLocationAsync(_db, branchId, ct);

            var q = _db.Set<StockLocation>().AsNoTracking();
            if (!includeInactive) q = q.Where(l => l.IsActive);
            // X-Branch-Id => that branch's locations plus the shared (BranchId null) ones; no header => everything.
            if (branchId.HasValue) q = q.Where(l => l.BranchId == null || l.BranchId == branchId.Value);
            var list = await q.OrderByDescending(l => l.IsDefault).ThenBy(l => l.BranchId == null ? 0 : 1).ThenBy(l => l.Name).ToListAsync(ct);
            return list.Select(StockOpsSupport.ToDto).ToList();
        }

        public async Task<StockLocationDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var l = await _db.Set<StockLocation>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return l == null ? null : StockOpsSupport.ToDto(l);
        }

        /// <summary>Default location for the caller's branch (X-Branch-Id), else the global default.</summary>
        public async Task<StockLocationDto> GetOrCreateDefaultAsync(CancellationToken ct = default)
            => StockOpsSupport.ToDto(await StockOpsSupport.GetDefaultLocationAsync(_db, _user.BranchId, ct));

        public async Task<BaseResponse<StockLocationDto>> CreateAsync(StockLocationUpsertDto dto, CancellationToken ct = default)
        {
            var err = Validate(dto);
            if (err != null) return new(false, err, err);

            var code = dto.Code.Trim().ToUpperInvariant();
            if (await _db.Set<StockLocation>().AnyAsync(l => l.Code == code, ct))
                return new(false, "Code already in use.", "Code already in use.");

            await StockOpsSupport.EnsureDefaultLocationAsync(_db, null, ct);

            var loc = new StockLocation
            {
                Name = dto.Name.Trim(),
                Code = code,
                IsDefault = false,
                IsActive = dto.IsActive,
                BranchId = _user.BranchId,
                CreatedOn = DateTime.UtcNow
            };
            _db.Set<StockLocation>().Add(loc);

            if (dto.IsDefault) await MakeDefaultAsync(loc, ct);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Location created.", StockOpsSupport.ToDto(loc));
        }

        public async Task<BaseResponse<StockLocationDto>> UpdateAsync(int id, StockLocationUpsertDto dto, CancellationToken ct = default)
        {
            var err = Validate(dto);
            if (err != null) return new(false, err, err);

            var loc = await _db.Set<StockLocation>().FirstOrDefaultAsync(l => l.Id == id, ct);
            if (loc == null) return new(false, "Location not found.", "Location not found.");

            var code = dto.Code.Trim().ToUpperInvariant();
            if (await _db.Set<StockLocation>().AnyAsync(l => l.Code == code && l.Id != id, ct))
                return new(false, "Code already in use.", "Code already in use.");

            if (loc.IsDefault && !dto.IsActive)
                return new(false, "The default location cannot be deactivated.", "The default location cannot be deactivated.");
            if (loc.IsDefault && !dto.IsDefault)
                return new(false, "Pick another location as default instead of un-flagging this one.", "Pick another location as default instead of un-flagging this one.");

            loc.Name = dto.Name.Trim();
            loc.Code = code;
            loc.IsActive = dto.IsActive;
            if (dto.IsDefault && !loc.IsDefault) await MakeDefaultAsync(loc, ct);

            await _db.SaveChangesAsync(ct);
            return new(true, null, "Location updated.", StockOpsSupport.ToDto(loc));
        }

        public async Task<BaseResponse<bool>> DeactivateAsync(int id, CancellationToken ct = default)
        {
            var loc = await _db.Set<StockLocation>().FirstOrDefaultAsync(l => l.Id == id, ct);
            if (loc == null) return new(false, "Location not found.", "Location not found.");
            if (loc.IsDefault) return new(false, "The default location cannot be deactivated.", "The default location cannot be deactivated.");

            var hasStock = await _db.Set<LocationStock>().AnyAsync(s => s.LocationId == id && s.Quantity != 0, ct);
            if (hasStock) return new(false, "Transfer the remaining stock out before deactivating this location.", "Location still holds stock.");

            loc.IsActive = false;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Location deactivated.", true);
        }

        public async Task<List<LocationStockDto>> GetStockAsync(int locationId, string? search, CancellationToken ct = default)
        {
            var loc = await _db.Set<StockLocation>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == locationId, ct);
            if (loc == null) return new List<LocationStockDto>();

            var ingQ = _db.Set<Ingredient>().AsNoTracking().Where(i => i.IsActive);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                ingQ = ingQ.Where(i => i.Name.ToLower().Contains(s));
            }
            var ings = await ingQ.OrderBy(i => i.Name).Select(i => new { i.Id, i.Name, i.Unit, i.QuantityOnHand }).ToListAsync(ct);

            if (StockOpsSupport.MirrorsIngredient(loc))
                return ings.Select(i => new LocationStockDto(i.Id, i.Name, i.Unit, i.QuantityOnHand)).ToList();

            var ids = ings.Select(i => i.Id).ToList();
            var qty = await StockOpsSupport.GetQuantitiesAsync(_db, loc, ids, ct);
            return ings.Select(i => new LocationStockDto(i.Id, i.Name, i.Unit, qty[i.Id])).ToList();
        }

        private async Task MakeDefaultAsync(StockLocation loc, CancellationToken ct)
        {
            // Changing the default only re-points which location is "Main" for
            // its scope (the global one mirrors Ingredient.QuantityOnHand, a
            // branch one holds its own rows); it does not move any quantities.
            // Defaults are per scope: only the same branch (or the shared scope)
            // loses its flag.
            var others = await _db.Set<StockLocation>().Where(l => l.IsDefault && l.Id != loc.Id && l.BranchId == loc.BranchId).ToListAsync(ct);
            foreach (var o in others) o.IsDefault = false;
            loc.IsDefault = true;
            loc.IsActive = true;
            _logger.LogWarning("StockOps: default location changed to {Name} ({Id}) by {User}", loc.Name, loc.Id, _user.UserName);
        }

        private static string? Validate(StockLocationUpsertDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required.";
            if (string.IsNullOrWhiteSpace(dto.Code)) return "Code is required.";
            if (dto.Code.Trim().Length > 20) return "Code must be 20 characters or fewer.";
            return null;
        }
    }
}
