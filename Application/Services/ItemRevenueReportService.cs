using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    /// <summary>
    /// Item Revenue Report — per-item sales, cost and stock for a period.
    ///
    /// How a line's revenue is found (there is no price snapshot on
    /// TransactionItems, so it is reconstructed from what was actually paid):
    ///
    ///   • Item-only invoices (GameId == null): every line is weighted by its
    ///     list value (Item.Price × Qty + add-ons) and the invoice's real
    ///     TotalPrice is split by those weights. The sum of the lines always
    ///     equals the cash that hit the drawer — discounts, rounding and price
    ///     edits are all absorbed.
    ///   • Items on a game session (GameId != null): TotalPrice also contains
    ///     the play time, so the line is valued at list price less the
    ///     session's discount %. These sales were previously missing from the
    ///     report entirely.
    ///   • Event-kit lines (IsIncluded) are $0 revenue but DO leave the shelf,
    ///     so they count in COGS and are reported as "given free".
    ///
    /// COGS per unit: Item.BuyPrice when set; otherwise the recipe's
    /// ingredient cost at today's ingredient prices (F&amp;B). Items with
    /// neither report a cost of 0 and are flagged so the owner can fix them.
    ///
    /// Dates are instants: [From, To). The UI sends local-midnight bounds so a
    /// "day" is the venue's day, not UTC's.
    /// </summary>
    public class ItemRevenueReportService : IItemRevenueReportService
    {
        private readonly IBaseRepository<TransactionRecord> _txRepo;
        private readonly IBaseRepository<Item> _itemRepo;
        private readonly IBaseRepository<Category> _catRepo;
        private readonly IBaseRepository<RecipeLine> _recipeRepo;

        private const int PaidStatus = 6; // "Processed and Paid"
        private const int ItemEnabledStatus = 1; // Item status "Enabled" — everything else (Disabled/Deleted) is retired from the shelf

        public ItemRevenueReportService(
            IBaseRepository<TransactionRecord> txRepo,
            IBaseRepository<Item> itemRepo,
            IBaseRepository<Category> catRepo,
            IBaseRepository<RecipeLine> recipeRepo)
        {
            _txRepo = txRepo;
            _itemRepo = itemRepo;
            _catRepo = catRepo;
            _recipeRepo = recipeRepo;
        }

        private static DateTime AsUtc(DateTime d) =>
            d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
            : d.Kind == DateTimeKind.Local ? d.ToUniversalTime()
            : d;

        private static bool IsTcgCategory(Category c) =>
            string.Equals(c.ItemType?.Trim(), "Retail", StringComparison.OrdinalIgnoreCase)
            || (c.Name ?? "").Contains("tcg", StringComparison.OrdinalIgnoreCase);

        private sealed class Acc
        {
            public int Units;
            public int Free;
            public decimal Gross;
            public decimal Net;
            public decimal AddOnNet;
        }

        public async Task<ItemRevenueReportDto> GetReportAsync(
            ItemRevenueReportRequestDto request,
            CancellationToken ct = default)
        {
            DateTime? fromUtc = request.From.HasValue ? AsUtc(request.From.Value) : null;
            DateTime? toUtc = request.To.HasValue ? AsUtc(request.To.Value) : null;

            // ── 1. Categories in scope ───────────────────────────────────
            var catQuery = _catRepo.Query().Where(c => c.Type == "item");
            if (request.CategoryIds is { Count: > 0 })
                catQuery = catQuery.Where(c => request.CategoryIds.Contains(c.Id));

            var categories = await catQuery.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
            var categoryIds = categories.Select(c => c.Id).ToList();

            // ── 2. Items in those categories + recipe cost per item ──────
            var items = await _itemRepo.Query()
                .AsNoTracking()
                .Where(i => categoryIds.Contains(i.CategoryId))
                .Include(i => i.Category)
                .ToListAsync(ct);

            var itemIds = items.Select(i => i.Id).ToList();

            // Recipe cost = Σ ingredient qty × current BuyPricePerUnit. Items
            // with at least one recipe line are "recipe items" — their stock
            // lives on the ingredients, not on Item.Quantity.
            // Recipe qty is in the recipe line's unit (e.g. g) while the price
            // is per ingredient unit (e.g. kg) — convert exactly like
            // StockService does when it consumes stock.
            var recipeRows = await _recipeRepo.Query()
                .AsNoTracking()
                .Where(r => itemIds.Contains(r.ItemId))
                .Select(r => new
                {
                    r.ItemId,
                    r.Quantity,
                    RecipeUnit = r.Unit,
                    IngredientUnit = r.Ingredient.Unit,
                    UnitPrice = r.Ingredient.BuyPricePerUnit ?? 0m,
                })
                .ToListAsync(ct);

            var recipeCost = recipeRows
                .GroupBy(r => r.ItemId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Sum(x => UnitConverter.Convert(x.Quantity, x.RecipeUnit ?? x.IngredientUnit, x.IngredientUnit) * x.UnitPrice));

            // ── 3. Paid transactions in range that carry item lines ──────
            // Nested projection on purpose: a server-side SelectMany over
            // TransactionItems has produced duplicated rows on prod before
            // (see AccountingReportService). One row per transaction, lines
            // nested, then flattened in memory.
            var txQuery = _txRepo.Query().AsNoTracking()
                .Where(t => t.StatusId == PaidStatus && t.TransactionItems.Any());

            if (fromUtc.HasValue) txQuery = txQuery.Where(t => (t.PaidOn ?? t.CreatedOn) >= fromUtc.Value);
            if (toUtc.HasValue) txQuery = txQuery.Where(t => (t.PaidOn ?? t.CreatedOn) < toUtc.Value);

            if (request.CategoryIds is { Count: > 0 })
                txQuery = txQuery.Where(t => t.TransactionItems.Any(ti => categoryIds.Contains(ti.Item.CategoryId)));

            var txRows = await txQuery
                .Select(t => new
                {
                    t.Id,
                    t.TotalPrice,
                    IsGame = t.GameId != null,
                    // Unconditional like JournalService/AccountingReportService: a
                    // discount deactivated later must not rewrite history.
                    Pct = t.Discount != null ? t.Discount.Percentage : 0,
                    Lines = t.TransactionItems.Select(ti => new
                    {
                        ti.ItemId,
                        ti.Quantity,
                        ti.IsIncluded,
                        ListPrice = ti.Item.Price,
                        AddOnTotal = ti.AddOns.Sum(a => (decimal?)(a.UnitPrice * a.Quantity)) ?? 0m,
                    }).ToList(),
                })
                .ToListAsync(ct);

            // ── 4. Attribute money to items ──────────────────────────────
            var acc = new Dictionary<int, Acc>();
            Acc For(int itemId) => acc.TryGetValue(itemId, out var found) ? found : (acc[itemId] = new Acc());

            foreach (var tx in txRows)
            {
                var pct = tx.Pct is > 0 and < 100 ? tx.Pct : 0;
                var keep = 1m - pct / 100m;

                // List value of every line (kit lines are $0 by design).
                var weights = tx.Lines.Select(l => new
                {
                    l,
                    ItemPart = l.IsIncluded ? 0m : l.ListPrice * l.Quantity,
                    AddOnPart = l.IsIncluded ? 0m : l.AddOnTotal,
                }).ToList();

                var listTotal = weights.Sum(w => w.ItemPart + w.AddOnPart);

                // Item-only invoice: scale list → what was really paid.
                // Game session: list × (1 − discount) — TotalPrice includes play time.
                decimal scale = tx.IsGame
                    ? keep
                    : (listTotal > 0m ? tx.TotalPrice / listTotal : 0m);

                foreach (var w in weights)
                {
                    var slot = For(w.l.ItemId);
                    if (w.l.IsIncluded)
                    {
                        slot.Free += w.l.Quantity;
                        continue;
                    }

                    slot.Units += w.l.Quantity;

                    var net = (w.ItemPart + w.AddOnPart) * scale;
                    var addOnNet = w.AddOnPart * scale;
                    // Gross = what would have been billed with no discount.
                    var gross = keep > 0m ? net / keep : net;

                    slot.Net += net;
                    slot.AddOnNet += addOnNet;
                    slot.Gross += gross;
                }
            }

            // ── 5. Per-item lines ────────────────────────────────────────
            // Retired items (status Disabled or Deleted — the shop "disables"
            // items it no longer sells): they are not on the shelf, so their
            // stock is worth nothing whatever Item.Quantity still says.
            // They only stay in the report when they actually sold in the
            // period (history must not disappear); otherwise they are dropped.
            var itemLines = items.Select<Item, ItemRevenueLineDto?>(item =>
            {
                acc.TryGetValue(item.Id, out var sold);
                var units = sold?.Units ?? 0;
                var free = sold?.Free ?? 0;
                var isDeleted = item.StatusId != ItemEnabledStatus;
                if (isDeleted && units == 0 && free == 0) return null;
                var revenue = Math.Round(sold?.Net ?? 0m, 2);
                var gross = Math.Round(sold?.Gross ?? 0m, 2);
                var addOnRev = Math.Round(sold?.AddOnNet ?? 0m, 2);

                var isRecipe = recipeCost.ContainsKey(item.Id);
                decimal? unitCost = item.BuyPrice.HasValue
                    ? item.BuyPrice.Value
                    : isRecipe ? Math.Round(recipeCost[item.Id], 4) : null;
                var costSource = item.BuyPrice.HasValue ? "buy" : isRecipe ? "recipe" : "none";

                var cogs = Math.Round((unitCost ?? 0m) * (units + free), 2);
                var gp = revenue - cogs;

                // Recipe items: Item.Quantity is not a real shelf count.
                // Deleted items: nothing on the shelf.
                var stockQty = isRecipe || isDeleted ? 0 : item.Quantity;
                var stockBuy = !isRecipe && item.BuyPrice.HasValue ? Math.Round(item.BuyPrice.Value * stockQty, 2) : 0m;
                var stockSell = isRecipe ? 0m : Math.Round(item.Price * stockQty, 2);

                return new ItemRevenueLineDto
                {
                    ItemId = item.Id,
                    ItemName = item.Name,
                    CategoryId = item.CategoryId,
                    CategoryName = item.Category?.Name ?? string.Empty,
                    ImagePath = item.ImagePath,
                    SellPrice = item.Price,
                    BuyPrice = item.BuyPrice,
                    UnitCost = unitCost,
                    CostSource = costSource,
                    IsRecipe = isRecipe,
                    IsDeleted = isDeleted,
                    UnitsSold = units,
                    UnitsGivenFree = free,
                    GrossRevenue = gross,
                    DiscountGiven = Math.Round(gross - revenue, 2),
                    AddOnRevenue = addOnRev,
                    Revenue = revenue,
                    Cogs = cogs,
                    GrossProfit = gp,
                    GrossMarginPct = revenue > 0 ? Math.Round(gp / revenue * 100m, 1) : null,
                    StockOnHand = stockQty,
                    StockBuyValue = stockBuy,
                    StockSellValue = stockSell,
                    StockPotentialProfit = stockSell - stockBuy,
                };
            })
            .Where(l => l is not null)
            .Select(l => l!)
            .ToList();

            // ── 6. Group by category ─────────────────────────────────────
            var grouped = categories.Select(cat =>
            {
                var lines = itemLines
                    .Where(l => l.CategoryId == cat.Id)
                    .OrderByDescending(l => l.Revenue)
                    .ThenBy(l => l.ItemName)
                    .ToList();

                var rev = lines.Sum(l => l.Revenue);
                var cogs = lines.Sum(l => l.Cogs);
                var gp = rev - cogs;

                return new ItemRevenueCategoryGroupDto
                {
                    CategoryId = cat.Id,
                    CategoryName = cat.Name,
                    ItemType = cat.ItemType,
                    IsTcg = IsTcgCategory(cat),
                    Items = lines,
                    TotalUnitsSold = lines.Sum(l => l.UnitsSold),
                    TotalUnitsGivenFree = lines.Sum(l => l.UnitsGivenFree),
                    TotalGrossRevenue = lines.Sum(l => l.GrossRevenue),
                    TotalDiscount = lines.Sum(l => l.DiscountGiven),
                    TotalAddOnRevenue = lines.Sum(l => l.AddOnRevenue),
                    TotalRevenue = rev,
                    TotalCogs = cogs,
                    TotalGrossProfit = gp,
                    GrossMarginPct = rev > 0 ? Math.Round(gp / rev * 100m, 1) : null,
                    TotalStockBuyValue = lines.Sum(l => l.StockBuyValue),
                    TotalStockSellValue = lines.Sum(l => l.StockSellValue),
                    TotalStockPotentialProfit = lines.Sum(l => l.StockPotentialProfit),
                };
            })
            .Where(g => g.Items.Count > 0)
            .ToList();

            // ── 7. Totals ────────────────────────────────────────────────
            var grandRev = grouped.Sum(g => g.TotalRevenue);
            var grandCogs = grouped.Sum(g => g.TotalCogs);
            var grandGp = grandRev - grandCogs;

            var tcg = grouped.Where(g => g.IsTcg).ToList();
            var fnb = grouped.Where(g => !g.IsTcg).ToList();

            var tcgRev = tcg.Sum(g => g.TotalRevenue);
            var tcgCogs = tcg.Sum(g => g.TotalCogs);
            var fnbRev = fnb.Sum(g => g.TotalRevenue);
            var fnbCogs = fnb.Sum(g => g.TotalCogs);

            return new ItemRevenueReportDto
            {
                From = fromUtc,
                To = toUtc,
                GeneratedAt = DateTime.UtcNow,
                TransactionCount = txRows.Count,
                FilteredCategoryIds = categoryIds,
                Categories = grouped,

                GrandTotalUnitsSold = grouped.Sum(g => g.TotalUnitsSold),
                GrandTotalUnitsGivenFree = grouped.Sum(g => g.TotalUnitsGivenFree),
                GrandTotalGrossRevenue = grouped.Sum(g => g.TotalGrossRevenue),
                GrandTotalDiscount = grouped.Sum(g => g.TotalDiscount),
                GrandTotalAddOnRevenue = grouped.Sum(g => g.TotalAddOnRevenue),
                GrandTotalRevenue = grandRev,
                GrandTotalCogs = grandCogs,
                GrandTotalGrossProfit = grandGp,
                GrandGrossMarginPct = grandRev > 0 ? Math.Round(grandGp / grandRev * 100m, 1) : null,
                GrandTotalStockBuyValue = grouped.Sum(g => g.TotalStockBuyValue),
                GrandTotalStockSellValue = grouped.Sum(g => g.TotalStockSellValue),
                GrandTotalStockPotentialProfit = grouped.Sum(g => g.TotalStockPotentialProfit),

                TcgUnitsSold = tcg.Sum(g => g.TotalUnitsSold),
                TcgRevenue = tcgRev,
                TcgCogs = tcgCogs,
                TcgGrossProfit = tcgRev - tcgCogs,
                TcgMarginPct = tcgRev > 0 ? Math.Round((tcgRev - tcgCogs) / tcgRev * 100m, 1) : null,
                TcgStockBuyValue = tcg.Sum(g => g.TotalStockBuyValue),
                TcgStockSellValue = tcg.Sum(g => g.TotalStockSellValue),

                FnbUnitsSold = fnb.Sum(g => g.TotalUnitsSold),
                FnbRevenue = fnbRev,
                FnbCogs = fnbCogs,
                FnbGrossProfit = fnbRev - fnbCogs,
                FnbMarginPct = fnbRev > 0 ? Math.Round((fnbRev - fnbCogs) / fnbRev * 100m, 1) : null,
            };
        }
    }
}
