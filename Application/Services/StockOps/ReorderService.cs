using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.StockOps
{
    /// <summary>
    /// Reorder suggestions: an ingredient is suggested when its quantity at
    /// the default location (Ingredient.QuantityOnHand) is at or below the
    /// rule's MinQty, or Ingredient.ReorderLevel when it has no rule.
    ///   suggested = TargetQty - qty            (rule)
    ///   suggested = 2 * ReorderLevel - qty     (no rule)
    /// </summary>
    public class ReorderService : IReorderService
    {
        private readonly ApplicationDbContext _db;

        public ReorderService(ApplicationDbContext db) { _db = db; }

        public async Task<List<ReorderSuggestionDto>> GetSuggestionsAsync(CancellationToken ct = default)
        {
            var ingredients = await _db.Set<Ingredient>().AsNoTracking()
                .Where(i => i.IsActive)
                .Select(i => new { i.Id, i.Name, i.Unit, i.QuantityOnHand, i.ReorderLevel, i.BuyPricePerUnit })
                .ToListAsync(ct);

            var rules = await _db.Set<ReorderRule>().AsNoTracking()
                .Where(r => r.IsActive)
                .ToDictionaryAsync(r => r.IngredientId, ct);

            var candidates = new List<(int Id, string Name, string Unit, decimal Qty, decimal Threshold, decimal Suggested, decimal? BuyPrice, ReorderRule? Rule)>();
            foreach (var i in ingredients)
            {
                rules.TryGetValue(i.Id, out var rule);
                decimal? threshold = rule != null ? rule.MinQty : i.ReorderLevel;
                if (!threshold.HasValue || threshold.Value <= 0) continue;
                if (i.QuantityOnHand > threshold.Value) continue;

                var target = rule != null ? rule.TargetQty : 2m * i.ReorderLevel!.Value;
                var suggested = Math.Round(Math.Max(0m, target - i.QuantityOnHand), 3);
                if (suggested <= 0) continue;
                candidates.Add((i.Id, i.Name, i.Unit, i.QuantityOnHand, threshold.Value, suggested, i.BuyPricePerUnit, rule));
            }
            if (candidates.Count == 0) return new List<ReorderSuggestionDto>();

            var ids = candidates.Select(c => c.Id).ToList();

            // Latest purchase line per ingredient (date, then id).
            var lastLines = await _db.Set<PurchaseLine>().AsNoTracking()
                .Where(pl => ids.Contains(pl.IngredientId))
                .Select(pl => new
                {
                    pl.IngredientId, pl.UnitCost, pl.Purchase.PurchaseDate, pl.Purchase.SupplierId,
                    SupplierName = pl.Purchase.Supplier != null ? pl.Purchase.Supplier.Name : null,
                    PurchaseId = pl.PurchaseId, pl.Id
                })
                .ToListAsync(ct);
            var last = lastLines
                .GroupBy(l => l.IngredientId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.PurchaseDate).ThenByDescending(x => x.Id).First());

            var supplierIds = candidates.Where(c => c.Rule?.PreferredSupplierId != null).Select(c => c.Rule!.PreferredSupplierId!.Value).Distinct().ToList();
            var suppliers = supplierIds.Count == 0
                ? new Dictionary<int, string>()
                : await _db.Set<Supplier>().AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

            var result = new List<ReorderSuggestionDto>();
            foreach (var c in candidates)
            {
                last.TryGetValue(c.Id, out var lp);
                decimal? unitCost = lp?.UnitCost ?? c.BuyPrice;
                int? supplierId = c.Rule?.PreferredSupplierId ?? lp?.SupplierId;
                string? supplierName = c.Rule?.PreferredSupplierId != null
                    ? suppliers.GetValueOrDefault(c.Rule.PreferredSupplierId.Value)
                    : lp?.SupplierName;

                result.Add(new ReorderSuggestionDto(
                    c.Id, c.Name, c.Unit, c.Qty, c.Threshold, c.Suggested,
                    unitCost,
                    unitCost.HasValue ? Math.Round(c.Suggested * unitCost.Value, 2) : 0m,
                    supplierId, supplierName,
                    lp?.PurchaseDate, lp?.UnitCost,
                    c.Rule?.LeadTimeDays ?? 0,
                    c.Rule != null));
            }

            return result.OrderByDescending(r => r.EstimatedCost).ThenBy(r => r.IngredientName).ToList();
        }

        public async Task<List<ReorderRuleDto>> ListRulesAsync(CancellationToken ct = default)
        {
            var rules = await _db.Set<ReorderRule>().AsNoTracking().OrderBy(r => r.Id).ToListAsync(ct);
            return await ToDtosAsync(rules, ct);
        }

        public async Task<ReorderRuleDto?> GetRuleAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<ReorderRule>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return r == null ? null : (await ToDtosAsync(new[] { r }, ct)).First();
        }

        public async Task<BaseResponse<ReorderRuleDto>> CreateRuleAsync(ReorderRuleUpsertDto dto, CancellationToken ct = default)
        {
            var err = await ValidateAsync(dto, ct);
            if (err != null) return new(false, err, err);
            if (await _db.Set<ReorderRule>().AnyAsync(r => r.IngredientId == dto.IngredientId, ct))
                return new(false, "This ingredient already has a reorder rule. Edit it instead.", "Rule exists.");

            var r = new ReorderRule
            {
                IngredientId = dto.IngredientId,
                MinQty = dto.MinQty,
                TargetQty = dto.TargetQty,
                PreferredSupplierId = dto.PreferredSupplierId,
                LeadTimeDays = Math.Max(0, dto.LeadTimeDays),
                IsActive = dto.IsActive,
                CreatedOn = DateTime.UtcNow
            };
            _db.Set<ReorderRule>().Add(r);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Rule created.", (await ToDtosAsync(new[] { r }, ct)).First());
        }

        public async Task<BaseResponse<ReorderRuleDto>> UpdateRuleAsync(int id, ReorderRuleUpsertDto dto, CancellationToken ct = default)
        {
            var r = await _db.Set<ReorderRule>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r == null) return new(false, "Rule not found.", "Rule not found.");
            var err = await ValidateAsync(dto, ct);
            if (err != null) return new(false, err, err);
            if (await _db.Set<ReorderRule>().AnyAsync(x => x.IngredientId == dto.IngredientId && x.Id != id, ct))
                return new(false, "Another rule already covers this ingredient.", "Rule exists.");

            r.IngredientId = dto.IngredientId;
            r.MinQty = dto.MinQty;
            r.TargetQty = dto.TargetQty;
            r.PreferredSupplierId = dto.PreferredSupplierId;
            r.LeadTimeDays = Math.Max(0, dto.LeadTimeDays);
            r.IsActive = dto.IsActive;
            r.ModifiedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Rule updated.", (await ToDtosAsync(new[] { r }, ct)).First());
        }

        public async Task<bool> DeleteRuleAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<ReorderRule>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r == null) return false;
            _db.Set<ReorderRule>().Remove(r);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<BaseResponse<PurchaseCreateDto>> BuildPurchaseDraftAsync(ReorderPurchaseDraftRequestDto request, CancellationToken ct = default)
        {
            var suggestions = await GetSuggestionsAsync(ct);
            if (request.IngredientIds != null && request.IngredientIds.Count > 0)
            {
                var set = request.IngredientIds.ToHashSet();
                suggestions = suggestions.Where(s => set.Contains(s.IngredientId)).ToList();
            }
            if (request.SupplierId.HasValue)
                suggestions = suggestions.Where(s => s.SupplierId == request.SupplierId.Value).ToList();

            if (suggestions.Count == 0)
                return new(false, "Nothing to reorder for the selected filters.", "Nothing to reorder.");

            int? supplierId = request.SupplierId;
            if (!supplierId.HasValue)
            {
                var distinct = suggestions.Select(s => s.SupplierId).Where(s => s.HasValue).Distinct().ToList();
                if (distinct.Count == 1) supplierId = distinct[0];
            }

            var lines = suggestions.Select(s => new PurchaseLineInputDto(
                s.IngredientId,
                s.SuggestedQty,
                s.UnitCost ?? 0m,
                s.SupplierName != null && s.SupplierId != supplierId ? $"Usually from {s.SupplierName}" : "Reorder suggestion")).ToList();

            var draft = new PurchaseCreateDto(
                supplierId,
                DateTime.UtcNow,
                null,
                string.IsNullOrWhiteSpace(request.Notes) ? $"Prepared from reorder suggestions on {DateTime.UtcNow:yyyy-MM-dd}" : request.Notes.Trim(),
                lines);
            return new(true, null, "Draft ready.", draft);
        }

        private async Task<string?> ValidateAsync(ReorderRuleUpsertDto dto, CancellationToken ct)
        {
            if (dto.MinQty < 0) return "Minimum quantity cannot be negative.";
            if (dto.TargetQty <= dto.MinQty) return "Target quantity must be greater than the minimum.";
            if (!await _db.Set<Ingredient>().AnyAsync(i => i.Id == dto.IngredientId, ct)) return "Ingredient not found.";
            if (dto.PreferredSupplierId.HasValue && !await _db.Set<Supplier>().AnyAsync(s => s.Id == dto.PreferredSupplierId.Value, ct))
                return "Supplier not found.";
            return null;
        }

        private async Task<List<ReorderRuleDto>> ToDtosAsync(IEnumerable<ReorderRule> rules, CancellationToken ct)
        {
            var list = rules.ToList();
            var ingIds = list.Select(r => r.IngredientId).Distinct().ToList();
            var supIds = list.Where(r => r.PreferredSupplierId.HasValue).Select(r => r.PreferredSupplierId!.Value).Distinct().ToList();
            var ings = await _db.Set<Ingredient>().AsNoTracking().Where(i => ingIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Name, i.Unit }).ToDictionaryAsync(i => i.Id, ct);
            var sups = supIds.Count == 0
                ? new Dictionary<int, string>()
                : await _db.Set<Supplier>().AsNoTracking().Where(s => supIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);

            return list.Select(r => new ReorderRuleDto(
                r.Id, r.IngredientId,
                ings.TryGetValue(r.IngredientId, out var i) ? i.Name : $"#{r.IngredientId}",
                ings.TryGetValue(r.IngredientId, out var u) ? u.Unit : "",
                r.MinQty, r.TargetQty, r.PreferredSupplierId,
                r.PreferredSupplierId.HasValue ? sups.GetValueOrDefault(r.PreferredSupplierId.Value) : null,
                r.LeadTimeDays, r.IsActive)).OrderBy(r => r.IngredientName).ToList();
        }
    }
}
