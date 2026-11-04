using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.StockOps
{
    /// <summary>
    /// Per-branch stock position and purchase receiving. Reads the same data the
    /// locations / transfers already maintain: the global default location mirrors
    /// Ingredient.QuantityOnHand, every other location (branch defaults included)
    /// lives in StockOps_LocationStocks. Receiving a purchase at a branch is a
    /// posted StockTransfer Main -> location, so PurchaseService is never touched.
    /// </summary>
    public class BranchStockService : IBranchStockService
    {
        public const string PurchaseReferencePrefix = "PO-";

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly IStockTransferService _transfers;
        private readonly ILogger<BranchStockService> _logger;

        public BranchStockService(ApplicationDbContext db, ICurrentUserAccessor user, IStockTransferService transfers, ILogger<BranchStockService> logger)
        {
            _db = db; _user = user; _transfers = transfers; _logger = logger;
        }

        private sealed record IngredientInfo(int Id, string Name, string Unit, decimal QuantityOnHand, decimal? ReorderLevel, decimal? BuyPricePerUnit);

        private async Task<List<IngredientInfo>> ActiveIngredientsAsync(CancellationToken ct)
            => await _db.Set<Ingredient>().AsNoTracking()
                .Where(i => i.IsActive).OrderBy(i => i.Name)
                .Select(i => new IngredientInfo(i.Id, i.Name, i.Unit, i.QuantityOnHand, i.ReorderLevel, i.BuyPricePerUnit))
                .ToListAsync(ct);

        /// <summary>Effective reorder level: active rule MinQty, else Ingredient.ReorderLevel.</summary>
        private async Task<Dictionary<int, decimal>> ReorderLevelsAsync(IEnumerable<IngredientInfo> ingredients, CancellationToken ct)
        {
            var rules = await _db.Set<ReorderRule>().AsNoTracking().Where(r => r.IsActive)
                .Select(r => new { r.IngredientId, r.MinQty }).ToListAsync(ct);
            var byRule = rules.GroupBy(r => r.IngredientId).ToDictionary(g => g.Key, g => g.First().MinQty);
            var result = new Dictionary<int, decimal>();
            foreach (var i in ingredients)
            {
                if (byRule.TryGetValue(i.Id, out var min)) result[i.Id] = min;
                else if (i.ReorderLevel.HasValue) result[i.Id] = i.ReorderLevel.Value;
            }
            return result;
        }

        private static bool Below(decimal qty, decimal? level) => level.HasValue && level.Value > 0 && qty <= level.Value;

        /// <summary>LocationStock summed per ingredient over the given location ids.</summary>
        private async Task<Dictionary<int, decimal>> SumLocationStockAsync(IReadOnlyCollection<int> locationIds, CancellationToken ct)
        {
            if (locationIds.Count == 0) return new Dictionary<int, decimal>();
            var rows = await _db.Set<LocationStock>().AsNoTracking()
                .Where(s => locationIds.Contains(s.LocationId))
                .Select(s => new { s.IngredientId, s.Quantity })
                .ToListAsync(ct);
            return rows.GroupBy(r => r.IngredientId).ToDictionary(g => g.Key, g => g.Sum(r => r.Quantity));
        }

        public async Task<BranchStockPositionDto> GetBranchPositionAsync(int? branchId, CancellationToken ct = default)
        {
            branchId ??= _user.BranchId;
            Branch? branch = null;
            if (branchId.HasValue)
            {
                branch = await _db.Set<Branch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId.Value, ct);
                if (branch == null) branchId = null; // unknown branch => shared scope
            }

            var def = await StockOpsSupport.GetDefaultLocationAsync(_db, branchId, ct);
            var others = await _db.Set<StockLocation>().AsNoTracking()
                .Where(l => l.Id != def.Id && l.BranchId == branchId && l.IsActive)
                .OrderBy(l => l.Name).ToListAsync(ct);

            var ingredients = await ActiveIngredientsAsync(ct);
            var levels = await ReorderLevelsAsync(ingredients, ct);
            var ids = ingredients.Select(i => i.Id).ToList();
            var defQty = await StockOpsSupport.GetQuantitiesAsync(_db, def, ids, ct);
            var otherQty = await SumLocationStockAsync(others.Select(l => l.Id).ToList(), ct);

            var lines = new List<BranchStockLineDto>();
            foreach (var i in ingredients)
            {
                var d = defQty.GetValueOrDefault(i.Id, 0m);
                var o = otherQty.GetValueOrDefault(i.Id, 0m);
                var total = Math.Round(d + o, 3);
                decimal? level = levels.TryGetValue(i.Id, out var lv) ? lv : null;
                var value = i.BuyPricePerUnit.HasValue ? Math.Round(total * i.BuyPricePerUnit.Value, 2) : 0m;
                lines.Add(new BranchStockLineDto(i.Id, i.Name, i.Unit, d, o, total, level, Below(total, level), i.BuyPricePerUnit, value));
            }

            return new BranchStockPositionDto(
                branchId,
                branch?.Code ?? StockOpsSupport.DefaultLocationCode,
                branch?.Name ?? "Shared (Main)",
                def.Id, def.Name,
                others.Select(StockOpsSupport.ToDto).ToList(),
                lines.Count(l => l.BelowLevel),
                Math.Round(lines.Sum(l => l.Value), 2),
                lines);
        }

        public async Task<StockPositionsDto> GetPositionsAsync(CancellationToken ct = default)
        {
            var branches = await _db.Set<Branch>().AsNoTracking()
                .Where(b => b.IsActive)
                .OrderByDescending(b => b.IsDefault).ThenBy(b => b.Name)
                .ToListAsync(ct);

            // Shared scope first, then every active branch.
            var scopes = new List<int?> { null };
            scopes.AddRange(branches.Select(b => (int?)b.Id));

            var positions = new List<BranchStockPositionDto>();
            foreach (var s in scopes) positions.Add(await GetBranchPositionAsync(s, ct));

            var ingredients = await ActiveIngredientsAsync(ct);
            var levels = await ReorderLevelsAsync(ingredients, ct);

            var columns = positions.Select(p => new StockPositionColumnDto(
                p.BranchId, p.BranchCode, p.BranchName, p.DefaultLocationId, p.DefaultLocationName, p.TotalValue, p.BelowLevelCount)).ToList();

            var rows = new List<StockPositionRowDto>();
            foreach (var i in ingredients)
            {
                decimal? level = levels.TryGetValue(i.Id, out var lv) ? lv : null;
                var cells = new List<StockPositionCellDto>();
                foreach (var p in positions)
                {
                    var line = p.Lines.FirstOrDefault(l => l.IngredientId == i.Id);
                    var qty = line?.TotalQty ?? 0m;
                    cells.Add(new StockPositionCellDto(p.BranchId, qty, Below(qty, level), line?.Value ?? 0m));
                }
                var total = Math.Round(cells.Sum(c => c.Qty), 3);
                rows.Add(new StockPositionRowDto(i.Id, i.Name, i.Unit, level, i.BuyPricePerUnit, cells, total,
                    i.BuyPricePerUnit.HasValue ? Math.Round(total * i.BuyPricePerUnit.Value, 2) : 0m));
            }

            return new StockPositionsDto(columns, rows, Math.Round(rows.Sum(r => r.TotalValue), 2));
        }

        public async Task<BaseResponse<ReceiveToLocationResultDto>> ReceiveToLocationAsync(ReceiveToLocationDto dto, CancellationToken ct = default)
        {
            var purchase = await _db.Set<Purchase>().AsNoTracking().Include(p => p.Lines)
                .FirstOrDefaultAsync(p => p.Id == dto.PurchaseId, ct);
            if (purchase == null) return new(false, "Purchase not found.", "Purchase not found.");

            var loc = await _db.Set<StockLocation>().AsNoTracking().FirstOrDefaultAsync(l => l.Id == dto.LocationId, ct);
            if (loc == null) return new(false, "Location not found.", "Location not found.");
            if (!loc.IsActive) return new(false, "Location is inactive.", "Location is inactive.");
            if (StockOpsSupport.MirrorsIngredient(loc))
                return new(false, "Purchases already land at the main location; pick a branch or sub-location.", "Location is the global default.");

            var reference = PurchaseReferencePrefix + purchase.Id;

            // Idempotent: a purchase is received once. A cancelled attempt does not count.
            var existing = await _db.Set<StockTransfer>().AsNoTracking()
                .Where(t => t.Reference == reference && t.Status != StockOpsSupport.StatusCancelled)
                .OrderBy(t => t.Id).FirstOrDefaultAsync(ct);
            if (existing != null)
            {
                var existingDto = await _transfers.GetAsync(existing.Id, ct);
                var where = existingDto?.ToLocationName ?? loc.Name;
                return new(true, null, $"Purchase #{purchase.Id} was already received at {where} ({existing.Reference}).",
                    new ReceiveToLocationResultDto(purchase.Id, existing.ToLocationId, where, true, existingDto!));
            }

            var lines = purchase.Lines.Where(l => l.Quantity > 0)
                .Select(l => new StockTransferLineInputDto(l.IngredientId, l.Quantity)).ToList();
            if (lines.Count == 0) return new(false, "Purchase has no lines to receive.", "Purchase has no lines.");

            var main = await StockOpsSupport.EnsureDefaultLocationAsync(_db, null, ct);
            if (main.Id == loc.Id)
                return new(false, "Purchases already land at the main location; pick a branch or sub-location.", "Location is the global default.");

            var notes = string.IsNullOrWhiteSpace(dto.Notes)
                ? $"Receive purchase #{purchase.Id}{(string.IsNullOrWhiteSpace(purchase.InvoiceNumber) ? "" : $" (invoice {purchase.InvoiceNumber})")} to {loc.Name}"
                : dto.Notes.Trim();

            var created = await _transfers.CreateAsync(new StockTransferCreateDto(main.Id, loc.Id, notes, lines), ct);
            if (!created.Success || created.Data == null) return new(false, created.Error ?? created.Message, created.Message);

            var transfer = await _db.Set<StockTransfer>().FirstAsync(t => t.Id == created.Data.Id, ct);
            transfer.Reference = reference;
            await _db.SaveChangesAsync(ct);

            var posted = await _transfers.PostAsync(transfer.Id, ct);
            if (!posted.Success || posted.Data == null)
            {
                // Leave no half-received draft behind so a retry can succeed later.
                transfer.Status = StockOpsSupport.StatusCancelled;
                await _db.SaveChangesAsync(ct);
                _logger.LogWarning("StockOps: receive purchase #{Purchase} to {Loc} failed: {Err}", purchase.Id, loc.Name, posted.Error);
                return new(false, posted.Error ?? posted.Message, posted.Message);
            }

            _logger.LogInformation("StockOps: purchase #{Purchase} received at {Loc} via {Ref} by {User}", purchase.Id, loc.Name, reference, _user.UserName);
            return new(true, null, $"Purchase #{purchase.Id} received at {loc.Name} ({reference}).",
                new ReceiveToLocationResultDto(purchase.Id, loc.Id, loc.Name, false, posted.Data));
        }
    }
}
