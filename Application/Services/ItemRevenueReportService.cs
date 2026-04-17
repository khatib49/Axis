using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class ItemRevenueReportService : IItemRevenueReportService
    {
        private readonly IBaseRepository<TransactionRecord> _txRepo;
        private readonly IBaseRepository<Item> _itemRepo;
        private readonly IBaseRepository<Category> _catRepo;

        // TCG category names — same keyword used throughout the system
        private static readonly string[] TcgKeywords = { "tcg", "tcg retail" };

        public ItemRevenueReportService(
            IBaseRepository<TransactionRecord> txRepo,
            IBaseRepository<Item> itemRepo,
            IBaseRepository<Category> catRepo)
        {
            _txRepo = txRepo;
            _itemRepo = itemRepo;
            _catRepo = catRepo;
        }

        public async Task<ItemRevenueReportDto> GetReportAsync(
            ItemRevenueReportRequestDto request,
            CancellationToken ct = default)
        {
            //var toExclusive = request.To?.Date.AddDays(1);
            // ── Date boundaries — always UTC ─────────────────────────────
            DateTime? fromUtc = request.From.HasValue
                ? DateTime.SpecifyKind(request.From.Value.Date, DateTimeKind.Utc)
                : null;

            DateTime? toUtc = request.To.HasValue
                ? DateTime.SpecifyKind(request.To.Value.Date.AddDays(1), DateTimeKind.Utc)
                : null;

            // ── 1. Load all item categories (type = 'item') ──────────────
            var catQuery = _catRepo.Query().Where(c => c.Type == "item");
            if (request.CategoryIds is { Count: > 0 })
                catQuery = catQuery.Where(c => request.CategoryIds.Contains(c.Id));

            var categories = await catQuery
                .OrderBy(c => c.Name)
                .ToListAsync(ct);

            var categoryIds = categories.Select(c => c.Id).ToList();

            // ── 2. Load all items in those categories ────────────────────
            var items = await _itemRepo.Query()
                .Where(i => categoryIds.Contains(i.CategoryId))
                .Include(i => i.Category)
                .ToListAsync(ct);

            // ── 3. Load all completed item transactions in date range ────
            var txQuery = _txRepo.Query()
                .Where(t => t.StatusId == 6 && t.GameId == null);

            if (fromUtc.HasValue)
                txQuery = txQuery.Where(t => t.CreatedOn >= fromUtc.Value);
            if (toUtc.HasValue)
                txQuery = txQuery.Where(t => t.CreatedOn < toUtc.Value);

            // Only transactions that contain at least one item in our category filter
            if (categoryIds.Count > 0)
                txQuery = txQuery.Where(t =>
                    t.TransactionItems.Any(ti =>
                        ti.Item != null && categoryIds.Contains(ti.Item.CategoryId)));

            var transactions = await txQuery
                .Include(t => t.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                        .ThenInclude(i => i.Category)
                .ToListAsync(ct);

            // ── 4. Aggregate per item ────────────────────────────────────
            // For each transaction, compute proportional revenue per item
            // (mirrors the journal service logic: use TotalPrice not item prices)

            // itemId → { UnitsSold, Revenue }
            var soldMap = new Dictionary<int, (int Units, decimal Revenue)>();

            foreach (var tx in transactions)
            {
                var txItems = tx.TransactionItems
                    .Where(ti => ti.Item != null && categoryIds.Contains(ti.Item.CategoryId))
                    .ToList();

                if (!txItems.Any()) continue;

                // Full price sum (pre-discount) for proportional distribution
                var fullSum = tx.TransactionItems
                    .Where(ti => ti.Item != null)
                    .Sum(ti => ti.Item!.Price * ti.Quantity);

                if (fullSum == 0) continue;

                foreach (var ti in txItems)
                {
                    var fullLine = ti.Item!.Price * ti.Quantity;
                    var proportion = fullLine / fullSum;
                    // Proportional share of TotalPrice (discount-aware)
                    var allocatedRevenue = tx.TotalPrice * proportion;

                    if (!soldMap.ContainsKey(ti.ItemId))
                        soldMap[ti.ItemId] = (0, 0m);

                    var existing = soldMap[ti.ItemId];
                    soldMap[ti.ItemId] = (existing.Units + ti.Quantity, existing.Revenue + allocatedRevenue);
                }
            }

            // ── 5. Build per-item lines ──────────────────────────────────
            var itemLines = items.Select(item =>
            {
                soldMap.TryGetValue(item.Id, out var sold);
                var unitsSold = sold.Units;
                var revenue = Math.Round(sold.Revenue, 2);
                var cogs = item.BuyPrice.HasValue
                    ? Math.Round(item.BuyPrice.Value * unitsSold, 2)
                    : 0m;
                var grossProfit = revenue - cogs;
                var marginPct = revenue > 0
                    ? Math.Round(grossProfit / revenue * 100, 1)
                    : (decimal?)null;

                var stockBuyValue = item.BuyPrice.HasValue
                    ? Math.Round(item.BuyPrice.Value * item.Quantity, 2)
                    : 0m;
                var stockSellValue = Math.Round(item.Price * item.Quantity, 2);

                return new ItemRevenueLineDto
                {
                    ItemId = item.Id,
                    ItemName = item.Name,
                    CategoryId = item.CategoryId,
                    CategoryName = item.Category?.Name ?? string.Empty,
                    ImagePath = item.ImagePath,
                    SellPrice = item.Price,
                    BuyPrice = item.BuyPrice,
                    UnitsSold = unitsSold,
                    Revenue = revenue,
                    Cogs = cogs,
                    GrossProfit = grossProfit,
                    GrossMarginPct = marginPct,
                    StockOnHand = item.Quantity,
                    StockBuyValue = stockBuyValue,
                    StockSellValue = stockSellValue,
                    StockPotentialProfit = stockSellValue - stockBuyValue,
                };
            }).ToList();

            // ── 6. Group by category ─────────────────────────────────────
            var grouped = categories.Select(cat =>
            {
                var lines = itemLines
                    .Where(l => l.CategoryId == cat.Id)
                    .OrderByDescending(l => l.Revenue)
                    .ToList();

                var totalRev = lines.Sum(l => l.Revenue);
                var totalCogs = lines.Sum(l => l.Cogs);
                var totalGP = totalRev - totalCogs;

                return new ItemRevenueCategoryGroupDto
                {
                    CategoryId = cat.Id,
                    CategoryName = cat.Name,
                    Items = lines,
                    TotalUnitsSold = lines.Sum(l => l.UnitsSold),
                    TotalRevenue = totalRev,
                    TotalCogs = totalCogs,
                    TotalGrossProfit = totalGP,
                    GrossMarginPct = totalRev > 0
                        ? Math.Round(totalGP / totalRev * 100, 1)
                        : null,
                    TotalStockBuyValue = lines.Sum(l => l.StockBuyValue),
                    TotalStockSellValue = lines.Sum(l => l.StockSellValue),
                    TotalStockPotentialProfit = lines.Sum(l => l.StockPotentialProfit),
                };
            })
            // Only include categories that have items
            .Where(g => g.Items.Any())
            .ToList();

            // ── 7. Grand totals ──────────────────────────────────────────
            var grandRev = grouped.Sum(g => g.TotalRevenue);
            var grandCogs = grouped.Sum(g => g.TotalCogs);
            var grandGP = grandRev - grandCogs;

            // TCG summary: all categories whose name contains "tcg"
            var tcgGroups = grouped.Where(g =>
                TcgKeywords.Any(kw =>
                    g.CategoryName.Trim().ToLower().Contains(kw))).ToList();

            var report = new ItemRevenueReportDto
            {
                From = fromUtc.Value,
                To = toUtc.Value,
                FilteredCategoryIds = categoryIds,
                Categories = grouped,
                GrandTotalUnitsSold = grouped.Sum(g => g.TotalUnitsSold),
                GrandTotalRevenue = grandRev,
                GrandTotalCogs = grandCogs,
                GrandTotalGrossProfit = grandGP,
                GrandGrossMarginPct = grandRev > 0
                    ? Math.Round(grandGP / grandRev * 100, 1)
                    : null,
                GrandTotalStockBuyValue = grouped.Sum(g => g.TotalStockBuyValue),
                GrandTotalStockSellValue = grouped.Sum(g => g.TotalStockSellValue),
                GrandTotalStockPotentialProfit = grouped.Sum(g => g.TotalStockPotentialProfit),
                TcgRevenue = tcgGroups.Sum(g => g.TotalRevenue),
                TcgCogs = tcgGroups.Sum(g => g.TotalCogs),
                TcgGrossProfit = tcgGroups.Sum(g => g.TotalGrossProfit),
                TcgStockBuyValue = tcgGroups.Sum(g => g.TotalStockBuyValue),
                TcgStockSellValue = tcgGroups.Sum(g => g.TotalStockSellValue),
            };

            return report;
        }
    }

}
