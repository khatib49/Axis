using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.MenuEngineering
{
    /// <summary>
    /// Recipe costing + menu engineering (Kasavana–Smith matrix).
    ///
    /// Sales per item (units, realised revenue) are taken from the existing
    /// Item Revenue report so both screens always agree on what sold: paid
    /// invoices (status 6) bucketed by PaidOn ?? CreatedOn, discounts and
    /// add-on / variant snapshots already attributed per line.
    ///
    /// Cost per unit, unlike that report, prefers the RECIPE: Σ RecipeLine
    /// quantity converted to the ingredient's stock unit (g → kg, ml → l via
    /// UnitConverter) × Ingredient.BuyPricePerUnit. Items without a recipe fall
    /// back to Item.BuyPrice (packaged goods) and otherwise cost 0 — both are
    /// flagged (HasRecipe=false, listed in MissingRecipes).
    ///
    /// Classification (per item, over the items in scope):
    ///   popular     ⇔ units ≥ 0.7 × (total units / item count)       (classic 70 % rule)
    ///   profitable  ⇔ margin % ≥ units-weighted average margin % of the menu
    ///   Star = popular & profitable, Plowhorse = popular & not, Puzzle = profitable & not popular, Dog = neither.
    /// </summary>
    public class MenuEngineeringService : IMenuEngineeringService
    {
        public const decimal PopularityFactor = 0.7m;
        public const decimal DefaultMarginThresholdPct = 60m;
        public const decimal DefaultTargetMarginPct = 70m;

        private readonly ApplicationDbContext _db;
        private readonly IItemRevenueReportService _revenue;
        private readonly ILogger<MenuEngineeringService> _log;

        public MenuEngineeringService(ApplicationDbContext db, IItemRevenueReportService revenue, ILogger<MenuEngineeringService> log)
        {
            _db = db;
            _revenue = revenue;
            _log = log;
        }

        private sealed record RecipeRow(int ItemId, int IngredientId, string IngredientName, decimal Quantity, string? Unit, string IngredientUnit, decimal? UnitPrice);

        private sealed record ItemCost(bool HasRecipe, decimal RecipeCost, List<ItemCostBreakdownLineDto> Lines, List<string> Unpriced);

        public async Task<MenuEngineeringReportDto> GetReportAsync(
            DateTime? from, DateTime? to,
            IReadOnlyCollection<int>? categoryIds = null,
            int? branchId = null,
            decimal marginThresholdPct = DefaultMarginThresholdPct,
            CancellationToken ct = default)
        {
            if (marginThresholdPct < 0 || marginThresholdPct > 100) marginThresholdPct = DefaultMarginThresholdPct;
            if (branchId.HasValue)
                _log.LogDebug("Menu engineering: branchId {BranchId} received but sales are not branch-scoped yet", branchId);

            var revenue = await _revenue.GetReportAsync(new ItemRevenueReportRequestDto
            {
                From = from,
                To = to,
                CategoryIds = categoryIds?.ToList(),
            }, ct);

            var sold = revenue.Categories
                .SelectMany(g => g.Items.Select(i => (Group: g, Line: i)))
                .ToList();

            var itemIds = sold.Select(s => s.Line.ItemId).Distinct().ToList();
            var costs = await LoadCostsAsync(itemIds, ct);

            // ── per-item raw numbers ─────────────────────────────────────
            var raw = sold.Select(s =>
            {
                costs.TryGetValue(s.Line.ItemId, out var c);
                var hasRecipe = c?.HasRecipe ?? false;
                var recipeCost = hasRecipe ? Math.Round(c!.RecipeCost, 4) : 0m;
                var costSource = hasRecipe ? "recipe" : s.Line.BuyPrice.HasValue ? "buy" : "none";
                var unitCost = hasRecipe ? recipeCost : (s.Line.BuyPrice ?? 0m);
                var price = s.Line.SellPrice;
                var gm = price - unitCost;
                var marginPct = price > 0 ? Math.Round(gm / price * 100m, 1) : 0m;
                var totalCost = Math.Round(unitCost * s.Line.UnitsSold, 2);
                return new
                {
                    s.Line.ItemId,
                    s.Line.ItemName,
                    s.Group.CategoryId,
                    s.Group.CategoryName,
                    Price = price,
                    RecipeCost = recipeCost,
                    HasRecipe = hasRecipe,
                    UnitCost = unitCost,
                    CostSource = costSource,
                    GrossMargin = gm,
                    MarginPct = marginPct,
                    s.Line.UnitsSold,
                    s.Line.Revenue,
                    TotalCost = totalCost,
                    Contribution = Math.Round(s.Line.Revenue - totalCost, 2),
                };
            }).ToList();

            // ── menu-wide thresholds ─────────────────────────────────────
            var itemCount = raw.Count;
            var totalUnits = raw.Sum(r => r.UnitsSold);
            var avgUnits = itemCount > 0 ? (decimal)totalUnits / itemCount : 0m;
            var popularityThreshold = Math.Round(PopularityFactor * avgUnits, 3);

            var listValue = raw.Sum(r => r.Price * r.UnitsSold);
            var listMargin = raw.Sum(r => r.GrossMargin * r.UnitsSold);
            var avgMarginPct = listValue > 0
                ? Math.Round(listMargin / listValue * 100m, 1)
                : itemCount > 0 ? Math.Round(raw.Average(r => r.MarginPct), 1) : 0m;

            var items = raw.Select(r =>
            {
                var popular = avgUnits > 0 && r.UnitsSold >= popularityThreshold;
                var profitable = r.GrossMargin > 0 && r.MarginPct >= avgMarginPct;
                var quadrant = (popular, profitable) switch
                {
                    (true, true) => "Star",
                    (true, false) => "Plowhorse",
                    (false, true) => "Puzzle",
                    _ => "Dog",
                };
                return new MenuEngineeringItemDto(
                    r.ItemId, r.ItemName, r.CategoryId, r.CategoryName, r.Price,
                    r.RecipeCost, r.HasRecipe, r.UnitCost, r.CostSource,
                    Math.Round(r.GrossMargin, 2), r.MarginPct,
                    r.UnitsSold, r.Revenue, r.TotalCost, r.Contribution,
                    PopularityIndex: avgUnits > 0 ? Math.Round(r.UnitsSold / avgUnits, 2) : 0m,
                    ProfitabilityIndex: avgMarginPct != 0 ? Math.Round(r.MarginPct / avgMarginPct, 2) : 0m,
                    IsPopular: popular,
                    IsProfitable: profitable,
                    Quadrant: quadrant,
                    LowMarginFlag: r.MarginPct < marginThresholdPct);
            })
            .OrderByDescending(i => i.Contribution)
            .ThenBy(i => i.Name)
            .ToList();

            var totRevenue = items.Sum(i => i.Revenue);
            var totCost = items.Sum(i => i.TotalCost);
            var totContribution = totRevenue - totCost;

            var totals = new MenuEngineeringTotalsDto(
                Revenue: totRevenue,
                Cost: totCost,
                Contribution: totContribution,
                AvgMarginPct: totRevenue > 0 ? Math.Round(totContribution / totRevenue * 100m, 1) : 0m,
                ItemCount: itemCount,
                UnitsSold: totalUnits,
                Stars: items.Count(i => i.Quadrant == "Star"),
                Plowhorses: items.Count(i => i.Quadrant == "Plowhorse"),
                Puzzles: items.Count(i => i.Quadrant == "Puzzle"),
                Dogs: items.Count(i => i.Quadrant == "Dog"),
                LowMarginCount: items.Count(i => i.LowMarginFlag));

            return new MenuEngineeringReportDto(
                From: revenue.From,
                To: revenue.To,
                GeneratedAt: DateTime.UtcNow,
                MarginThresholdPct: marginThresholdPct,
                AvgUnitsPerItem: Math.Round(avgUnits, 3),
                PopularityThresholdUnits: popularityThreshold,
                ProfitabilityThresholdPct: avgMarginPct,
                Items: items,
                Totals: totals,
                MissingRecipes: items.Where(i => !i.HasRecipe).Select(i => i.Name).OrderBy(n => n).ToList());
        }

        public async Task<ItemCostBreakdownDto?> GetItemCostBreakdownAsync(int itemId, decimal targetMarginPct = DefaultTargetMarginPct, CancellationToken ct = default)
        {
            if (targetMarginPct < 0 || targetMarginPct >= 100) targetMarginPct = DefaultTargetMarginPct;

            var item = await _db.Set<Item>().AsNoTracking()
                .Where(i => i.Id == itemId)
                .Select(i => new { i.Id, i.Name, Category = i.Category.Name, i.Price, i.BuyPrice })
                .FirstOrDefaultAsync(ct);
            if (item is null) return null;

            var costs = await LoadCostsAsync(new[] { itemId }, ct);
            costs.TryGetValue(itemId, out var c);
            var hasRecipe = c?.HasRecipe ?? false;
            var recipeCost = hasRecipe ? Math.Round(c!.RecipeCost, 4) : 0m;
            var costSource = hasRecipe ? "recipe" : item.BuyPrice.HasValue ? "buy" : "none";
            var unitCost = hasRecipe ? recipeCost : (item.BuyPrice ?? 0m);
            var gm = item.Price - unitCost;
            var marginPct = item.Price > 0 ? Math.Round(gm / item.Price * 100m, 1) : 0m;

            var suggested = SuggestPrice(unitCost, targetMarginPct);

            return new ItemCostBreakdownDto(
                item.Id, item.Name, item.Category, item.Price,
                hasRecipe, recipeCost, unitCost, costSource,
                Math.Round(gm, 2), marginPct,
                c?.Lines ?? new List<ItemCostBreakdownLineDto>(),
                targetMarginPct,
                suggested,
                Math.Ceiling(suggested * 4m) / 4m,
                c?.Unpriced ?? new List<string>());
        }

        /// <summary>Price that leaves <paramref name="targetMarginPct"/> of itself as margin over <paramref name="unitCost"/>.</summary>
        public static decimal SuggestPrice(decimal unitCost, decimal targetMarginPct)
        {
            if (targetMarginPct < 0 || targetMarginPct >= 100) targetMarginPct = DefaultTargetMarginPct;
            return Math.Round(unitCost / (1m - targetMarginPct / 100m), 2);
        }

        /// <summary>Σ line cost per item, with the per-line breakdown. Items without recipe lines are absent from the result.</summary>
        private async Task<Dictionary<int, ItemCost>> LoadCostsAsync(IReadOnlyCollection<int> itemIds, CancellationToken ct)
        {
            if (itemIds.Count == 0) return new();

            var rows = await _db.Set<RecipeLine>().AsNoTracking()
                .Where(r => itemIds.Contains(r.ItemId))
                .OrderBy(r => r.Id)
                .Select(r => new RecipeRow(
                    r.ItemId, r.IngredientId, r.Ingredient.Name, r.Quantity, r.Unit,
                    r.Ingredient.Unit, r.Ingredient.BuyPricePerUnit))
                .ToListAsync(ct);

            var result = new Dictionary<int, ItemCost>();
            foreach (var g in rows.GroupBy(r => r.ItemId))
            {
                var lines = new List<ItemCostBreakdownLineDto>();
                var unpriced = new List<string>();
                foreach (var r in g)
                {
                    var recipeUnit = string.IsNullOrWhiteSpace(r.Unit) ? r.IngredientUnit : r.Unit!;
                    var qty = UnitConverter.Convert(r.Quantity, recipeUnit, r.IngredientUnit, out var converted);
                    if (!converted)
                        _log.LogWarning("Menu engineering: cannot convert {Qty} {From} to {To} for ingredient {Ingredient} (item {ItemId}); using quantity as-is",
                            r.Quantity, recipeUnit, r.IngredientUnit, r.IngredientName, r.ItemId);
                    if (!r.UnitPrice.HasValue) unpriced.Add(r.IngredientName);
                    var lineCost = Math.Round(qty * (r.UnitPrice ?? 0m), 4);
                    lines.Add(new ItemCostBreakdownLineDto(
                        r.IngredientId, r.IngredientName, r.Quantity, recipeUnit,
                        Math.Round(qty, 4), r.IngredientUnit, r.UnitPrice, lineCost, converted));
                }
                result[g.Key] = new ItemCost(true, lines.Sum(l => l.LineCost), lines, unpriced);
            }
            return result;
        }
    }
}
