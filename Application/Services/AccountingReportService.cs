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

            // ── 3. Manual entries (Expense table) ──────────────────────
            // Filter by CreatedOn (when the row was entered into the system),
            // not by the FromDate/ToDate of the expense period. This matches
            // how Revenue is filtered (TransactionRecord.CreatedOn) so the
            // whole dashboard speaks the same date language. Per Rami's CR:
            // "all the data should be based on filter reporting period" —
            // CreatedOn is the rule.
            //
            // We also pull the mapped Account + AccountType + AccountNumber so we
            // can classify each row by what it actually is (Expense / Equity /
            // Revenue / Asset / Liability) — not by the fact that it lives in
            // the `expenses` table. This is what stops "Omar cash out" (an
            // Equity draw) and "Toters income" (a Revenue line) from being
            // counted as operating expenses.
            var expQ = _expenseRepo.Query()
                .Where(e => e.Category != null);

            if (from.HasValue)
                expQ = expQ.Where(e => e.CreatedOn >= from.Value.Date);
            if (toExclusive.HasValue)
                expQ = expQ.Where(e => e.CreatedOn < toExclusive.Value);

            var manualEntries = await expQ
                .Select(e => new
                {
                    CategoryName = e.Category.Name,
                    IsCapital = e.Category.IsCapital,
                    Amount = e.Amount,
                    AccountTypeName = e.Category.Account != null && e.Category.Account.AccountType != null
                        ? e.Category.Account.AccountType.TypeName
                        : null,
                    AccountNumber = e.Category.Account != null
                        ? e.Category.Account.AccountNumber
                        : null
                })
                .ToListAsync(ct);

            // Real expenses only: either explicitly mapped to an Expense-type
            // account, OR not mapped at all (legacy rows — treat as expense so
            // they still show up somewhere until they're remapped).
            bool IsExpenseLike(string? typeName)
                => typeName == null || string.Equals(typeName, "Expense", StringComparison.OrdinalIgnoreCase);

            var operatingLines = manualEntries
                .Where(x => !x.IsCapital && IsExpenseLike(x.AccountTypeName))
                .GroupBy(x => x.CategoryName)
                .Select(g => new ExpenseCategoryLineDto(g.Key, g.Sum(x => x.Amount)))
                .OrderByDescending(x => x.Amount)
                .ToList();

            var capitalLines = manualEntries
                .Where(x => x.IsCapital && IsExpenseLike(x.AccountTypeName))
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

            // ── 3b. Classify by AccountType ────────────────────────────
            // Bucket every manual entry by the AccountType of its mapped
            // Account. Unmapped rows fall under "Unmapped" so they remain
            // visible — they're typically what the user wants to clean up.
            var byTypeLines = manualEntries
                .GroupBy(x => x.AccountTypeName ?? "Unmapped")
                .Select(g => new AccountTypeLineDto(
                    AccountTypeName: g.Key,
                    Amount: g.Sum(x => x.Amount),
                    Categories: g
                        .GroupBy(x => x.CategoryName)
                        .Select(cg => new ExpenseCategoryLineDto(cg.Key, cg.Sum(x => x.Amount)))
                        .OrderByDescending(c => c.Amount)
                        .ToList()
                ))
                .OrderByDescending(t => t.Amount)
                .ToList();

            decimal SumByType(string type) =>
                byTypeLines.Where(l => string.Equals(l.AccountTypeName, type, StringComparison.OrdinalIgnoreCase))
                           .Sum(l => l.Amount);

            var byAccountType = new AccountTypeBreakdownDto(
                Asset: SumByType("Asset"),
                Liability: SumByType("Liability"),
                Equity: SumByType("Equity"),
                Revenue: SumByType("Revenue"),
                Expense: SumByType("Expense"),
                Lines: byTypeLines
            );

            // ── 3c. Subtotals by account-number prefix ─────────────────
            // Useful sanity check: "5000-5999 Expense", "3000-3999 Equity", etc.
            // Rows with no mapped account get bucketed under "Unmapped".
            static (string label, string prefix) RangeFor(string? accountNumber)
            {
                if (string.IsNullOrWhiteSpace(accountNumber)) return ("Unmapped", "?");
                var head = accountNumber.Trim()[0];
                return head switch
                {
                    '1' => ("1000-1999 Asset", "1"),
                    '2' => ("2000-2999 Liability", "2"),
                    '3' => ("3000-3999 Equity", "3"),
                    '4' => ("4000-4999 Revenue", "4"),
                    '5' => ("5000-5999 Expense", "5"),
                    _ => ($"{head}000-{head}999", head.ToString())
                };
            }

            var byRange = manualEntries
                .Select(x => new { Range = RangeFor(x.AccountNumber), x.Amount })
                .GroupBy(x => x.Range)
                .Select(g => new AccountRangeLineDto(
                    RangeLabel: g.Key.label,
                    Prefix: g.Key.prefix,
                    Amount: g.Sum(x => x.Amount)))
                .OrderBy(r => r.Prefix)
                .ToList();

            // ── 4. Net Income ────────────────────────────────────────────
            // Note: operatingExpenses.Total now excludes Equity/Revenue
            // misclassifications, so Net Income is no longer dragged down by
            // owner draws (Omar cash out) or inflated by mis-bucketed revenue.
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
                NetMarginPercent: netMargin,
                ByAccountType: byAccountType,
                ByAccountNumberRange: byRange
            );
        }

        public async Task<List<ExpenseCategoryLineDto>> GetExpensesBreakdownAsync(DateTime? from, DateTime? to, bool capitalOnly, CancellationToken ct = default)
        {
            // Mirror the dashboard rule: only return rows that are actual
            // expenses (mapped to an Expense-type account, or unmapped legacy
            // rows). Equity draws and Revenue lines should not appear in an
            // "Expenses Breakdown" report.
            //
            // Filter by CreatedOn (when the row was entered) to match the
            // dashboard's reporting-period filter.
            var toExclusive = to?.Date.AddDays(1);
            var expQ = _expenseRepo.Query()
                .Where(e => e.Category != null)
                .Where(e => e.Category.IsCapital == capitalOnly);

            if (from.HasValue)
                expQ = expQ.Where(e => e.CreatedOn >= from.Value.Date);
            if (toExclusive.HasValue)
                expQ = expQ.Where(e => e.CreatedOn < toExclusive.Value);

            var lines = await expQ
                .Select(e => new
                {
                    CategoryName = e.Category.Name,
                    e.Amount,
                    AccountTypeName = e.Category.Account != null && e.Category.Account.AccountType != null
                        ? e.Category.Account.AccountType.TypeName
                        : null
                })
                .ToListAsync(ct);

            return lines
                .Where(x => x.AccountTypeName == null
                            || string.Equals(x.AccountTypeName, "Expense", StringComparison.OrdinalIgnoreCase))
                .GroupBy(x => x.CategoryName)
                .Select(g => new ExpenseCategoryLineDto(g.Key, g.Sum(x => x.Amount)))
                .OrderByDescending(x => x.Amount)
                .ToList();
        }
    }
}
