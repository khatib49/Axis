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
        private readonly IBaseRepository<JournalEntry> _journalRepo;
        private readonly IBaseRepository<JournalEntryLine> _journalLineRepo;
        private readonly IBaseRepository<Account> _accountRepo;
        private readonly IBaseRepository<StockMovement> _movementRepo;

        // TCG category IDs — items whose Category.Name contains "TCG" or "Card"
        // We identify TCG items by checking Item.Category name at query time
        private const string TcgCategoryKeyword = "TCG";

        public AccountingReportService(
            IBaseRepository<TransactionRecord> txRepo,
            IBaseRepository<Expense> expenseRepo,
            IBaseRepository<ExpenseCategory> catRepo,
            IBaseRepository<JournalEntry> journalRepo,
            IBaseRepository<JournalEntryLine> journalLineRepo,
            IBaseRepository<Account> accountRepo,
            IBaseRepository<StockMovement> movementRepo)
        {
            _txRepo = txRepo;
            _expenseRepo = expenseRepo;
            _catRepo = catRepo;
            _journalRepo = journalRepo;
            _journalLineRepo = journalLineRepo;
            _accountRepo = accountRepo;
            _movementRepo = movementRepo;
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

            // Helper: recover gross from net using the transaction's discount %.
            // pct >= 100 is degenerate (would divide by zero) so we treat it as
            // no discount. Identical to the rule the JE builder uses, so the
            // dashboard and chart of accounts speak the same numbers.
            static decimal GrossOf(decimal net, int pct)
            {
                if (pct <= 0 || pct >= 100) return net;
                var factor = 1m - (pct / 100m);
                return Math.Round(net / factor, 2);
            }

            // Gaming revenue: TotalPrice (net) + the same row's discount % so
            // we can compute gross alongside net in a single pass.
            var gamingRows = await txQ
                .Where(t => t.GameId != null)
                .Select(t => new
                {
                    t.TotalPrice,
                    Pct = t.Discount != null ? t.Discount.Percentage : 0
                })
                .ToListAsync(ct);
            var gamingRevenue = gamingRows.Sum(r => r.TotalPrice);
            var gamingGross = gamingRows.Sum(r => GrossOf(r.TotalPrice, r.Pct));

            // FNB + TCG: load transactions with their items + discount %
            // and distribute proportionally across categories. We compute
            // both the net allocation (what hit cash) and the gross
            // allocation (what would have been billed before discount).
            var itemTxData = await txQ
                .Where(t => t.GameId == null)
                .Select(t => new
                {
                    t.TotalPrice,
                    Pct = t.Discount != null ? t.Discount.Percentage : 0,
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
            decimal fnbGross = 0m;
            decimal tcgGross = 0m;

            foreach (var tx in itemTxData)
            {
                var fullTotal = tx.Items.Sum(i => i.FullLineTotal);
                if (fullTotal == 0) continue;

                var txGross = GrossOf(tx.TotalPrice, tx.Pct);

                foreach (var item in tx.Items)
                {
                    var proportion = item.FullLineTotal / fullTotal;
                    var allocatedNet = tx.TotalPrice * proportion;
                    var allocatedGross = txGross * proportion;

                    if (item.CategoryName.Contains(TcgCategoryKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        tcgRevenue += allocatedNet;
                        tcgGross += allocatedGross;
                    }
                    else
                    {
                        fnbRevenue += allocatedNet;
                        fnbGross += allocatedGross;
                    }
                }
            }

            var totalRevenue = gamingRevenue + fnbRevenue + tcgRevenue;
            var totalGross = gamingGross + fnbGross + tcgGross;
            var discountsGiven = Math.Round(totalGross - totalRevenue, 2);

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

            // ── 2b. Ingredient COGS ────────────────────────────────────
            // Sum of StockMovement.TotalCost for Consumption movements in
            // the period. Each Consumption movement was created when an
            // F&B item was sold; TotalCost was snapshotted using the
            // ingredient's BuyPricePerUnit at that moment. So this is the
            // accurate cost-of-sales for FNB ingredients.
            //
            // Voided transactions create reversal movements with the
            // OPPOSITE sign on TotalCost so they net to zero — no special
            // handling needed here.
            var ingredientCogs = await _movementRepo.Query()
                .Where(m => m.Type == "Consumption"
                         && m.TotalCost != null
                         && (from == null || m.CreatedOn >= from.Value.Date)
                         && (toExclusive == null || m.CreatedOn < toExclusive.Value))
                .SumAsync(m => (decimal?)m.TotalCost, ct) ?? 0m;
            ingredientCogs = Math.Round(ingredientCogs, 2);

            var totalCogs = Math.Round(tcgCogs + ingredientCogs, 2);
            var foodCostPct = totalRevenue > 0
                ? Math.Round(ingredientCogs / totalRevenue * 100m, 1)
                : 0m;

            var cogs = new CogsSummaryDto(
                TcgCogs: tcgCogs,
                Total: totalCogs,
                IngredientCogs: ingredientCogs,
                FoodCostPercent: foodCostPct);

            var grossProfit = totalRevenue - totalCogs;

            // ── 3. Manual entries (Expense table) ──────────────────────
            // Filter by the expense's PERIOD (FromDate/ToDate), not by when it
            // was typed into the system. The expense's period is what the row
            // actually represents: "rent for March", "salaries for Q1", etc.
            //
            // An expense overlaps the reporting range when:
            //     e.FromDate <= filterTo  AND  e.ToDate >= filterFrom
            // So a rent entered once on June 5 covering Jan-Dec will show
            // under every monthly filter Jan through Dec, not only June.
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
                expQ = expQ.Where(e => e.ToDate >= from.Value.Date);
            if (to.HasValue)
                expQ = expQ.Where(e => e.FromDate <= to.Value.Date);

            var manualEntriesRaw = await expQ
                .Select(e => new
                {
                    CategoryName = e.Category.Name,
                    IsCapital = e.Category.IsCapital,
                    RawAmount = e.Amount,
                    FromDate = e.FromDate,
                    ToDate = e.ToDate,
                    AccountTypeName = e.Category.Account != null && e.Category.Account.AccountType != null
                        ? e.Category.Account.AccountType.TypeName
                        : null,
                    AccountNumber = e.Category.Account != null
                        ? e.Category.Account.AccountNumber
                        : null
                })
                .ToListAsync(ct);

            // ── Prorate ────────────────────────────────────────────────
            // For each expense, compute the portion of its amount that falls
            // inside the reporting period, by day-count overlap.
            //   prorated = raw * (overlapDays / expenseTotalDays)
            // So a $90,000 rent for Oct 2025 → Oct 2026 (366 days) shows up
            // as $7,500 in a 31-day March filter, and the full $90,000 only
            // when the filter spans the entire period.
            // If the filter has no bounds, the expense is shown at its raw
            // amount (no proration needed).
            static decimal ProrateByOverlap(decimal raw, DateTime eFrom, DateTime eTo, DateTime? fFrom, DateTime? fTo)
            {
                var eStart = eFrom.Date;
                var eEnd = eTo.Date;
                if (eEnd < eStart) eEnd = eStart;
                var totalDays = (decimal)((eEnd - eStart).TotalDays + 1);
                if (totalDays <= 0) return raw;

                var fStart = fFrom?.Date ?? eStart;
                var fEnd = fTo?.Date ?? eEnd;

                var overlapStart = eStart > fStart ? eStart : fStart;
                var overlapEnd = eEnd < fEnd ? eEnd : fEnd;
                if (overlapStart > overlapEnd) return 0m;

                var overlapDays = (decimal)((overlapEnd - overlapStart).TotalDays + 1);
                if (overlapDays >= totalDays) return raw;
                return Math.Round(raw * (overlapDays / totalDays), 2);
            }

            var manualEntries = manualEntriesRaw
                .Select(e => new
                {
                    e.CategoryName,
                    e.IsCapital,
                    Amount = ProrateByOverlap(e.RawAmount, e.FromDate, e.ToDate, from, to),
                    e.AccountTypeName,
                    e.AccountNumber
                })
                .ToList();

            // Real expenses only: either explicitly mapped to an Expense-type
            // account, OR not mapped at all (legacy rows — treat as expense so
            // they still show up somewhere until they're remapped).
            bool IsExpenseLike(string? typeName)
                => typeName == null || string.Equals(typeName, "Expense", StringComparison.OrdinalIgnoreCase);

            // Capital investments are accounted for as Assets (1500 Gaming
            // Equipment, 1510 Furniture, etc.) by convention. Some users map
            // them to Expense accounts instead; both are valid in practice.
            // Allow Expense, Asset, or unmapped — exclude only the obvious
            // misclassifications (Revenue / Equity / Liability).
            bool IsCapitalLike(string? typeName)
                => typeName == null
                || string.Equals(typeName, "Expense", StringComparison.OrdinalIgnoreCase)
                || string.Equals(typeName, "Asset", StringComparison.OrdinalIgnoreCase);

            var operatingLines = manualEntries
                .Where(x => !x.IsCapital && IsExpenseLike(x.AccountTypeName))
                .GroupBy(x => x.CategoryName)
                .Select(g => new ExpenseCategoryLineDto(g.Key, g.Sum(x => x.Amount)))
                .OrderByDescending(x => x.Amount)
                .ToList();

            var capitalLines = manualEntries
                .Where(x => x.IsCapital && IsCapitalLike(x.AccountTypeName))
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
                    Total: totalRevenue,
                    GamingGross: gamingGross,
                    FnbGross: fnbGross,
                    TcgGross: tcgGross,
                    TotalGross: totalGross,
                    DiscountsGiven: discountsGiven
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
            // Filter by the expense's PERIOD (FromDate/ToDate) — overlap with
            // the requested range. Mirrors the dashboard so the two never
            // disagree.
            var expQ = _expenseRepo.Query()
                .Where(e => e.Category != null)
                .Where(e => e.Category.IsCapital == capitalOnly);

            if (from.HasValue)
                expQ = expQ.Where(e => e.ToDate >= from.Value.Date);
            if (to.HasValue)
                expQ = expQ.Where(e => e.FromDate <= to.Value.Date);

            var linesRaw = await expQ
                .Select(e => new
                {
                    CategoryName = e.Category.Name,
                    RawAmount = e.Amount,
                    FromDate = e.FromDate,
                    ToDate = e.ToDate,
                    AccountTypeName = e.Category.Account != null && e.Category.Account.AccountType != null
                        ? e.Category.Account.AccountType.TypeName
                        : null
                })
                .ToListAsync(ct);

            // Mirror the dashboard's day-count proration so the breakdown
            // drilldown shows the same numbers as the dashboard total.
            static decimal ProrateByOverlap(decimal raw, DateTime eFrom, DateTime eTo, DateTime? fFrom, DateTime? fTo)
            {
                var eStart = eFrom.Date;
                var eEnd = eTo.Date;
                if (eEnd < eStart) eEnd = eStart;
                var totalDays = (decimal)((eEnd - eStart).TotalDays + 1);
                if (totalDays <= 0) return raw;
                var fStart = fFrom?.Date ?? eStart;
                var fEnd = fTo?.Date ?? eEnd;
                var overlapStart = eStart > fStart ? eStart : fStart;
                var overlapEnd = eEnd < fEnd ? eEnd : fEnd;
                if (overlapStart > overlapEnd) return 0m;
                var overlapDays = (decimal)((overlapEnd - overlapStart).TotalDays + 1);
                if (overlapDays >= totalDays) return raw;
                return Math.Round(raw * (overlapDays / totalDays), 2);
            }

            var lines = linesRaw
                .Select(x => new
                {
                    x.CategoryName,
                    Amount = ProrateByOverlap(x.RawAmount, x.FromDate, x.ToDate, from, to),
                    x.AccountTypeName
                })
                .ToList();

            // Capital breakdown allows Asset-mapped categories too (the proper
            // accounting treatment for capital investments); operating only
            // accepts Expense-type or unmapped.
            bool keep(string? typeName)
            {
                if (typeName == null) return true;
                if (string.Equals(typeName, "Expense", StringComparison.OrdinalIgnoreCase)) return true;
                if (capitalOnly && string.Equals(typeName, "Asset", StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }

            return lines
                .Where(x => keep(x.AccountTypeName))
                .GroupBy(x => x.CategoryName)
                .Select(g => new ExpenseCategoryLineDto(g.Key, g.Sum(x => x.Amount)))
                .OrderByDescending(x => x.Amount)
                .ToList();
        }

        // ===================================================================
        // Revenue coverage audit — surfaces orphan transactions (no JE) and
        // the live gap between the calculator (sum of TotalPrice) and the
        // chart of accounts.
        // ===================================================================
        public async Task<RevenueCoverageAuditDto> GetRevenueCoverageAuditAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var toExclusive = to?.Date.AddDays(1);

            // 1) Paid transactions in window (status=6 only — same as JE-create rule).
            var txQ = _txRepo.Query()
                .Where(t => t.StatusId == 6 && t.TotalPrice > 0);
            if (from.HasValue) txQ = txQ.Where(t => t.CreatedOn >= from.Value.Date);
            if (toExclusive.HasValue) txQ = txQ.Where(t => t.CreatedOn < toExclusive.Value);

            var txList = await txQ
                .Select(t => new { t.Id, t.TotalPrice, DiscountPct = (int?)(t.Discount != null ? t.Discount.Percentage : 0) })
                .ToListAsync(ct);

            var txIds = txList.Select(t => t.Id).ToList();

            // 2) Which transactions have a posted, non-voided JE?
            var jeTxIds = await _journalRepo.Query()
                .Where(je => je.ReferenceType == "Transaction"
                          && je.ReferenceId != null
                          && !je.IsVoided
                          && txIds.Contains(je.ReferenceId.Value))
                .Select(je => je.ReferenceId!.Value)
                .Distinct()
                .ToListAsync(ct);

            var jeSet = new HashSet<int>(jeTxIds);
            var orphanIds = txList.Where(t => !jeSet.Contains(t.Id)).Select(t => t.Id).ToList();

            // 3) Compute net + gross from the calculator side.
            decimal totalNet = txList.Sum(t => t.TotalPrice);
            decimal totalGross = txList.Sum(t =>
            {
                var pct = t.DiscountPct ?? 0;
                if (pct <= 0 || pct >= 100) return t.TotalPrice;
                var factor = 1m - (pct / 100m);
                return Math.Round(t.TotalPrice / factor, 2);
            });

            // 4) From the books: sum of credit balances on Revenue-type accounts
            // in window (4xxx) and debit balances on 4900 (Sales Discounts).
            var lineSums = await _journalLineRepo.Query()
                .Where(l => l.JournalEntry.IsPosted
                         && !l.JournalEntry.IsVoided
                         && l.JournalEntry.ReferenceType == "Transaction"
                         && (from == null || l.JournalEntry.EntryDate >= from.Value.Date)
                         && (toExclusive == null || l.JournalEntry.EntryDate < toExclusive.Value))
                .Select(l => new
                {
                    l.AccountId,
                    l.DebitAmount,
                    l.CreditAmount,
                    AccountNumber = l.Account.AccountNumber,
                    AccountTypeName = l.Account.AccountType.TypeName
                })
                .ToListAsync(ct);

            decimal revenueCredits = lineSums
                .Where(l => string.Equals(l.AccountTypeName, "Revenue", StringComparison.OrdinalIgnoreCase)
                         && l.AccountNumber != "4900")
                .Sum(l => l.CreditAmount);

            decimal salesDiscountsDebits = lineSums
                .Where(l => l.AccountNumber == "4900")
                .Sum(l => l.DebitAmount);

            decimal netOnBooks = revenueCredits - salesDiscountsDebits;
            decimal discrepancy = totalNet - netOnBooks;

            return new RevenueCoverageAuditDto(
                From: from,
                To: to,
                TransactionsCount: txList.Count,
                TransactionsTotalNet: totalNet,
                TransactionsTotalGross: totalGross,
                TransactionsWithJE: txList.Count - orphanIds.Count,
                TransactionsWithoutJE: orphanIds.Count,
                OrphanTransactionIds: orphanIds.OrderBy(i => i).Take(500).ToList(), // cap to avoid huge responses
                RevenueAccountsCredit: revenueCredits,
                SalesDiscountsDebit: salesDiscountsDebits,
                NetRevenueOnBooks: netOnBooks,
                Discrepancy: discrepancy
            );
        }
    }
}
