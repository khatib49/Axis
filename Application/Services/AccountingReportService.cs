using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class AccountingReportService : IAccountingReportService
    {
        private readonly IBaseRepository<TransactionRecord> _txRepo;
        private readonly IBaseRepository<Expense> _expenseRepo;
        private readonly IBaseRepository<ExpenseCategory> _catRepo;

        // TCG category IDs — items whose Category.Name contains "TCG" or "Card"
        // We identify TCG items by checking Item.Category name at query time
        private const string TcgCategoryKeyword = "TCG";

        public AccountingReportService(IBaseRepository<TransactionRecord> txRepo, IBaseRepository<Expense> expenseRepo, IBaseRepository<ExpenseCategory> catRepo)
        {
            _txRepo = txRepo;
            _expenseRepo = expenseRepo;
            _catRepo = catRepo;
        }

        public async Task<AccountingDashboardDto> GetDashboardAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var toExclusive = to?.Date.AddDays(1);

            // ── 1. Revenue ──────────────────────────────────────────────
            var txQ = _txRepo.Query().Where(t => t.StatusId == 6);

            if (from.HasValue)
                txQ = txQ.Where(t => t.CreatedOn >= from.Value.Date);
            if (toExclusive.HasValue)
                txQ = txQ.Where(t => t.CreatedOn < toExclusive.Value);

            // Gaming revenue: use TotalPrice directly (correct)
            var gamingRevenue = await txQ
                .Where(t => t.GameId != null)
                .SumAsync(t => (decimal?)t.TotalPrice, ct) ?? 0m;

            // FNB + TCG: load transactions with their items to split by category
            // Use TotalPrice as authoritative per-transaction total,
            // distribute proportionally across categories
            var itemTxData = await txQ
                .Where(t => t.GameId == null)
                .Select(t => new
                {
                    t.TotalPrice,
                    Items = t.TransactionItems.Select(ti => new
                    {
                        CategoryName = ti.Item != null && ti.Item.Category != null
                            ? ti.Item.Category.Name
                            : "",
                        FullLineTotal = (ti.Item != null ? ti.Item.Price : 0m) * ti.Quantity
                    })
                })
                .ToListAsync(ct);


            decimal fnbRevenue = 0m;
            decimal tcgRevenue = 0m;

            foreach (var tx in itemTxData)
            {
                var fullTotal = tx.Items.Sum(i => i.FullLineTotal);
                if (fullTotal == 0) continue;

                foreach (var item in tx.Items)
                {
                    // Proportional share of the actual TotalPrice paid
                    var proportion = item.FullLineTotal / fullTotal;
                    var allocated = tx.TotalPrice * proportion;

                    if (item.CategoryName.Contains(TcgCategoryKeyword, StringComparison.OrdinalIgnoreCase))
                        tcgRevenue += allocated;
                    else
                        fnbRevenue += allocated;
                }
            }

            var totalRevenue = gamingRevenue + fnbRevenue + tcgRevenue;

            // ── 2. COGS (TCG only — BuyPrice × Qty) ────────────────────
            var tcgCogsLines = await txQ
                .Where(t => t.GameId == null)
                .SelectMany(t => t.TransactionItems.Select(ti => new
                {
                    CategoryName = ti.Item != null && ti.Item.Category != null
                        ? ti.Item.Category.Name
                        : "",
                    BuyPrice = ti.Item != null ? ti.Item.BuyPrice : null,
                    Quantity = ti.Quantity
                }))
                .Where(x => x.CategoryName.Contains(TcgCategoryKeyword))
                .ToListAsync(ct);

            var tcgCogs = tcgCogsLines
                .Where(x => x.BuyPrice.HasValue)
                .Sum(x => x.BuyPrice!.Value * x.Quantity);

            var cogs = new CogsSummaryDto(TcgCogs: tcgCogs, Total: tcgCogs);

            var grossProfit = totalRevenue - tcgCogs;

            // ── 3. Expenses ─────────────────────────────────────────────
            // Filter expenses whose period overlaps the selected range.
            // An expense overlaps [from, to] if: expense.FromDate <= to AND expense.ToDate >= from
            var expQ = _expenseRepo.Query()
                .Where(e => e.Category != null);

            if (from.HasValue)
                expQ = expQ.Where(e => e.ToDate >= from.Value.Date);
            if (to.HasValue)
                expQ = expQ.Where(e => e.FromDate <= to.Value.Date);

            var expenseLines = await expQ
                .Select(e => new
                {
                    CategoryName = e.Category.Name,
                    IsCapital = e.Category.IsCapital,
                    e.Amount
                })
                .ToListAsync(ct);

            var operatingLines = expenseLines
                .Where(x => !x.IsCapital)
                .GroupBy(x => x.CategoryName)
                .Select(g => new ExpenseCategoryLineDto(g.Key, g.Sum(x => x.Amount)))
                .OrderByDescending(x => x.Amount)
                .ToList();

            var capitalLines = expenseLines
                .Where(x => x.IsCapital)
                .GroupBy(x => x.CategoryName)
                .Select(g => new ExpenseCategoryLineDto(g.Key, g.Sum(x => x.Amount)))
                .OrderByDescending(x => x.Amount)
                .ToList();

            var operatingExpenses = new ExpenseSummaryDto(
                Total: operatingLines.Sum(x => x.Amount),
                Lines: operatingLines
            );

            var capitalExpenses = new ExpenseSummaryDto(
                Total: capitalLines.Sum(x => x.Amount),
                Lines: capitalLines
            );

            // ── 4. Net Income ────────────────────────────────────────────
            var netIncome = grossProfit - operatingExpenses.Total;
            var netMargin = totalRevenue == 0 ? 0m
                : Math.Round(netIncome / totalRevenue * 100, 1);

            return new AccountingDashboardDto(
                From: from,
                To: to,
                Revenue: new RevenueBreakdownDto(
                    Gaming: gamingRevenue,
                    Fnb: fnbRevenue,
                    Tcg: tcgRevenue,
                    Total: totalRevenue
                ),
                OperatingExpenses: operatingExpenses,
                CapitalExpenses: capitalExpenses,
                Cogs: cogs,
                GrossProfit: grossProfit,
                NetIncome: netIncome,
                NetMarginPercent: netMargin
            );
        }

        public async Task<List<ExpenseCategoryLineDto>> GetExpensesBreakdownAsync(DateTime? from, DateTime? to, bool capitalOnly, CancellationToken ct = default)
        {
            var expQ = _expenseRepo.Query()
                .Where(e => e.Category != null)
                .Where(e => e.Category.IsCapital == capitalOnly);

            if (from.HasValue)
                expQ = expQ.Where(e => e.ToDate >= from.Value.Date);
            if (to.HasValue)
                expQ = expQ.Where(e => e.FromDate <= to.Value.Date);

            var lines = await expQ
                .Select(e => new
                {
                    CategoryName = e.Category.Name,
                    e.Amount
                })
                .ToListAsync(ct);

            return lines
                .GroupBy(x => x.CategoryName)
                .Select(g => new ExpenseCategoryLineDto(g.Key, g.Sum(x => x.Amount)))
                .OrderByDescending(x => x.Amount)
                .ToList();
        }
    }
}
