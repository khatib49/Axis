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
        // Event tickets never become TransactionRecords, so they need their
        // own read here or the dashboard silently under-reports revenue.
        private readonly IBaseRepository<EventRegistration> _eventRegRepo;
        private readonly IBaseRepository<Purchase> _purchaseRepo;
        private readonly IBaseRepository<IntegrationSetting> _settingsRepo;
        private readonly IBaseRepository<Ingredient> _ingredientRepo;
        private readonly IItemRevenueReportService _itemReport;

        // TCG category IDs — items whose Category.Name contains "TCG" or "Card"
        // We identify TCG items by checking Item.Category name at query time
        private const string TcgCategoryKeyword = "TCG";

        /// <summary>
        /// TCG / retail shelf goods = Category.ItemType "Retail" OR a name that
        /// still contains "tcg". Same rule as ItemRevenueReportService. Before
        /// 2026-09-27 only the NAME was checked, so renaming "TCG Pokemon" →
        /// "Pokemon" silently moved those sales into F&amp;B.
        /// </summary>
        private static bool IsTcg(string? categoryName, string? itemType) =>
            string.Equals(itemType?.Trim(), "Retail", StringComparison.OrdinalIgnoreCase)
            || (categoryName ?? "").Contains(TcgCategoryKeyword, StringComparison.OrdinalIgnoreCase);

        public AccountingReportService(
            IBaseRepository<TransactionRecord> txRepo,
            IBaseRepository<Expense> expenseRepo,
            IBaseRepository<ExpenseCategory> catRepo,
            IBaseRepository<JournalEntry> journalRepo,
            IBaseRepository<JournalEntryLine> journalLineRepo,
            IBaseRepository<Account> accountRepo,
            IBaseRepository<StockMovement> movementRepo,
            IBaseRepository<EventRegistration> eventRegRepo,
            IBaseRepository<Purchase> purchaseRepo,
            IBaseRepository<IntegrationSetting> settingsRepo,
            IBaseRepository<Ingredient> ingredientRepo,
            IItemRevenueReportService itemReport)
        {
            _purchaseRepo = purchaseRepo;
            _settingsRepo = settingsRepo;
            _ingredientRepo = ingredientRepo;
            _itemReport = itemReport;
            _txRepo = txRepo;
            _expenseRepo = expenseRepo;
            _catRepo = catRepo;
            _journalRepo = journalRepo;
            _journalLineRepo = journalLineRepo;
            _accountRepo = accountRepo;
            _movementRepo = movementRepo;
            _eventRegRepo = eventRegRepo;
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
                        ItemType = ti.Item != null && ti.Item.Category != null
                            ? ti.Item.Category.ItemType
                            : null,
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

                    if (IsTcg(item.CategoryName, item.ItemType))
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

            // ── 1b. Event ticket revenue ────────────────────────────────
            // Paid event registrations are the fourth revenue stream. They
            // live in their own table and are booked to 4300 Event Revenue,
            // so they have to be added here explicitly.
            //
            // The period filter uses ConfirmedOn — the moment the money was
            // actually recognised — matching the EntryDate the journal entry
            // uses, so the dashboard and the trial balance agree. Rows that
            // are somehow Paid without a ConfirmedOn stamp fall back to
            // CreatedOn rather than vanishing from the report.
            //
            // CAREFUL: EventRegistrations."ConfirmedOn"/"CreatedOn" are
            // TIMESTAMPTZ, and Npgsql refuses a DateTime with Kind=Unspecified
            // against those. `from`/`to` arrive from the query string, so their
            // Kind depends entirely on how the caller formatted the date —
            // normalise both bounds to UTC or the whole dashboard can throw.
            static DateTime AsUtc(DateTime d) =>
                d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
                : d.Kind == DateTimeKind.Local ? d.ToUniversalTime()
                : d;

            var evQ = _eventRegRepo.Query().Where(r => r.PaymentStatus == "Paid");

            if (from.HasValue)
            {
                var evFrom = AsUtc(from.Value.Date);
                evQ = evQ.Where(r => (r.ConfirmedOn ?? r.CreatedOn) >= evFrom);
            }
            if (toExclusive.HasValue)
            {
                var evTo = AsUtc(toExclusive.Value);
                evQ = evQ.Where(r => (r.ConfirmedOn ?? r.CreatedOn) < evTo);
            }

            var eventRevenue = Math.Round(
                await evQ.SumAsync(r => (decimal?)r.Amount, ct) ?? 0m, 2);

            var totalRevenue = gamingRevenue + fnbRevenue + tcgRevenue + eventRevenue;
            // Tickets are sold at a fixed price with no discount mechanism, so
            // gross == net for the events line.
            var totalGross = gamingGross + fnbGross + tcgGross + eventRevenue;
            var discountsGiven = Math.Round(totalGross - totalRevenue, 2);

            // ── 2. COGS (TCG only — BuyPrice × Qty) ────────────────────
            // IMPORTANT: use the same NESTED-projection pattern as the
            // revenue block above (line 81). A server-side SelectMany here
            // was over-counting COGS by ~2.6x on prod because EF's SQL
            // translation was producing duplicated line rows when other
            // navigations on TransactionRecord (Discount / TransactionAuditLog /
            // JournalEntries / KitchenBarOrders / etc.) got pulled in during
            // the flatten. Keeping it as `Select(t => new { Items = ... })`
            // produces one row per transaction with a nested items collection
            // and matches the Item Revenue Report exactly.
            var tcgCogsTxs = await txQ
                .Where(t => t.GameId == null)
                .Select(t => new
                {
                    Items = t.TransactionItems.Select(ti => new
                    {
                        CategoryName = ti.Item != null && ti.Item.Category != null
                            ? ti.Item.Category.Name
                            : "",
                        ItemType = ti.Item != null && ti.Item.Category != null
                            ? ti.Item.Category.ItemType
                            : null,
                        BuyPrice = ti.Item != null ? ti.Item.BuyPrice : null,
                        Quantity = ti.Quantity
                    })
                })
                .ToListAsync(ct);

            var tcgCogs = tcgCogsTxs
                .SelectMany(t => t.Items)
                .Where(x => x.BuyPrice.HasValue && IsTcg(x.CategoryName, x.ItemType))
                .Sum(x => x.BuyPrice!.Value * x.Quantity);

            // Event-kit lines live on GAME transactions (GameId != null), so
            // the query above never sees them — yet those boosters left the
            // shelf just as surely as a retail sale. Without this pass, every
            // kit handed out overstated gross profit by BuyPrice x Qty.
            // Same nested-projection shape as above (see the 2.6x warning).
            var kitCogsTxs = await txQ
                .Where(t => t.GameId != null
                         && t.TransactionItems.Any(ti => ti.IsIncluded))
                .Select(t => new
                {
                    Items = t.TransactionItems
                        .Where(ti => ti.IsIncluded)
                        .Select(ti => new
                        {
                            BuyPrice = ti.Item != null ? ti.Item.BuyPrice : null,
                            Quantity = ti.Quantity
                        })
                })
                .ToListAsync(ct);

            // No category filter here: whatever the event bundles is a cost of
            // running it. Recipe-backed items carry their cost through the
            // ingredient consumption block below instead, and their BuyPrice
            // is null, so nothing is counted twice.
            tcgCogs += kitCogsTxs
                .SelectMany(t => t.Items)
                .Where(x => x.BuyPrice.HasValue)
                .Sum(x => x.BuyPrice!.Value * x.Quantity);

            tcgCogs = Math.Round(tcgCogs, 2);

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
            // Deliberately excludes event revenue from the denominator: ticket
            // sales consume no ingredients, so folding them in would flatter
            // the food cost ratio and change the number the owner has been
            // tracking. This stays "ingredient cost as a share of SALES".
            var salesRevenue = totalRevenue - eventRevenue;
            var foodCostPct = salesRevenue > 0
                ? Math.Round(ingredientCogs / salesRevenue * 100m, 1)
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

            // ── 3d. Cash on Hand ────────────────────────────────────────
            // Rami (2026-09-11): Baseline + TOTAL revenue (all time) − TOTAL
            // expenses (all time — the "Total Expenses (All)" figure on the
            // Expenses page, i.e. every entry's raw amount, no proration).
            // Deliberately ignores the dashboard's date filter: it is the
            // till's running balance, not a period figure.
            var lifetimeSales = await _txRepo.Query()
                .Where(t => t.StatusId == 6)
                .SumAsync(t => (decimal?)t.TotalPrice, ct) ?? 0m;
            var lifetimeTickets = await _eventRegRepo.Query()
                .Where(r => r.PaymentStatus == "Paid")
                .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;
            var lifetimeRevenue = Math.Round(lifetimeSales + lifetimeTickets, 2);

            var lifetimeExpenses = Math.Round(
                await _expenseRepo.Query().SumAsync(e => (decimal?)e.Amount, ct) ?? 0m, 2);

            var baselineRaw = await _settingsRepo.Query()
                .Where(x => x.Key == "Accounting.CashOnHandBaseline")
                .Select(x => x.Value)
                .FirstOrDefaultAsync(ct);
            var baseline = decimal.TryParse(baselineRaw, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var b) ? b : 0m;

            var cashOnHand = new CashOnHandDto(
                Baseline: baseline,
                Revenue: lifetimeRevenue,
                OperatingExpenses: 0m,
                CapitalExpenses: 0m,
                OtherCashOut: 0m,
                StockPurchases: 0m,
                TotalExpenses: lifetimeExpenses,
                Amount: Math.Round(baseline + lifetimeRevenue - lifetimeExpenses, 2));

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
                    DiscountsGiven: discountsGiven,
                    Events: eventRevenue
                ),
                OperatingExpenses: operatingExpenses,
                CapitalExpenses: capitalExpenses,
                Cogs: cogs,
                GrossProfit: grossProfit,
                NetIncome: netIncome,
                NetMarginPercent: netMargin,
                ByAccountType: byAccountType,
                ByAccountNumberRange: byRange,
                CashOnHand: cashOnHand
            );
        }

        // ── Owner Summary drill-downs ───────────────────────────────────
        /// <summary>
        /// One endpoint behind every tile on the Owner Summary. Each metric
        /// answers "what is this number made of?" with the same period as
        /// the dashboard (cash on hand is all-time by definition).
        /// </summary>
        public async Task<MetricBreakdownDto> GetMetricBreakdownAsync(string metric, DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            metric = (metric ?? "").Trim().ToLowerInvariant();
            var dash = await GetDashboardAsync(from, to, ct);
            var toExclusive = to?.Date.AddDays(1);

            IQueryable<TransactionRecord> PaidTx()
            {
                var q = _txRepo.Query().Where(t => t.StatusId == 6);
                if (from.HasValue) q = q.Where(t => t.CreatedOn >= from.Value.Date);
                if (toExclusive.HasValue) q = q.Where(t => t.CreatedOn < toExclusive.Value);
                return q;
            }

            // Item revenue by category, discount-aware (invoice TotalPrice split
            // by list-price weight), split into Retail vs F&B by ItemType.
            async Task<(List<BreakdownRowDto> fnb, List<BreakdownRowDto> tcg)> ItemRevenueByCategoryAsync()
            {
                var rows = await PaidTx().Where(t => t.GameId == null)
                    .Select(t => new
                    {
                        t.Id, t.TotalPrice,
                        Lines = t.TransactionItems.Where(ti => ti.Item != null).Select(ti => new
                        {
                            Cat = ti.Item!.Category != null ? ti.Item.Category.Name : "(no category)",
                            ItemType = ti.Item!.Category != null ? ti.Item.Category.ItemType : null,
                            Full = ti.Item!.Price * ti.Quantity,
                            ti.Quantity,
                        }).ToList(),
                    }).ToListAsync(ct);

                var acc = new Dictionary<(string cat, bool tcg), (decimal amt, int qty, HashSet<int> tx)>();
                foreach (var r in rows)
                {
                    var full = r.Lines.Sum(l => l.Full);
                    if (full <= 0) continue;
                    foreach (var l in r.Lines)
                    {
                        var key = (l.Cat, IsTcg(l.Cat, l.ItemType));
                        (decimal amt, int qty, HashSet<int> tx) cur = acc.TryGetValue(key, out var v) ? v : (0m, 0, new HashSet<int>());
                        cur.amt += r.TotalPrice * (l.Full / full);
                        cur.qty += l.Quantity;
                        cur.tx.Add(r.Id);
                        acc[key] = cur;
                    }
                }
                List<BreakdownRowDto> Build(bool tcg) => acc.Where(k => k.Key.tcg == tcg)
                    .Select(k => new BreakdownRowDto(k.Key.cat, Math.Round(k.Value.amt, 2), k.Value.tx.Count, $"{k.Value.qty} units"))
                    .OrderByDescending(x => x.Amount).ToList();
                return (Build(false), Build(true));
            }

            async Task<List<BreakdownRowDto>> GamingByCategoryAsync()
            {
                var rows = await PaidTx().Where(t => t.GameId != null)
                    .GroupBy(t => t.Game != null && t.Game.Category != null ? t.Game.Category.Name : "(no category)")
                    .Select(g => new { Cat = g.Key, Amt = g.Sum(x => x.TotalPrice), Cnt = g.Count(), Hours = g.Sum(x => x.Hours) })
                    .ToListAsync(ct);
                return rows.OrderByDescending(r => r.Amt)
                    .Select(r => new BreakdownRowDto(r.Cat, Math.Round(r.Amt, 2), r.Cnt, $"{r.Hours:0.#} h")).ToList();
            }

            List<BreakdownRowDto> ExpenseRows(ExpenseSummaryDto e) =>
                e.Lines.Select(l => new BreakdownRowDto(l.Category, l.Amount)).ToList();

            switch (metric)
            {
                case "cash":
                {
                    var c = dash.CashOnHand!;
                    var expByCat = await _expenseRepo.Query()
                        .GroupBy(e => e.Category.Name)
                        .Select(g => new { Cat = g.Key, Amt = g.Sum(x => x.Amount), Cnt = g.Count() })
                        .ToListAsync(ct);
                    var rows = new List<BreakdownRowDto>
                    {
                        new("Baseline (till reading you set)", c.Baseline),
                        new("+ Total revenue — all paid sales & sessions (all time)", await _txRepo.Query().Where(t => t.StatusId == 6).SumAsync(t => (decimal?)t.TotalPrice, ct) ?? 0m),
                        new("+ Total revenue — paid event tickets (all time)", await _eventRegRepo.Query().Where(r => r.PaymentStatus == "Paid").SumAsync(r => (decimal?)r.Amount, ct) ?? 0m),
                    };
                    rows.AddRange(expByCat.OrderByDescending(x => x.Amt).Select(x => new BreakdownRowDto($"− Expenses · {x.Cat}", -x.Amt, x.Cnt)));
                    return new MetricBreakdownDto("cash", "Cash on Hand — how it is built", c.Amount, rows,
                        "All time, not affected by the date filter. Expenses = every entry on the Expenses page.", CountLabel: "entries");
                }
                case "revenue":
                {
                    var r = dash.Revenue;
                    var rows = new List<BreakdownRowDto>
                    {
                        new("Gaming (PS5, billiard, board games…)", r.Gaming),
                        new("F&B (food, drinks, tobacco)", r.Fnb),
                        new("TCG / Retail", r.Tcg),
                        new("Event tickets", r.Events),
                    };
                    return new MetricBreakdownDto("revenue", "Total Revenue by stream", r.Total, rows,
                        $"Net of discounts. Gross {r.TotalGross:0.00} − discounts {r.DiscountsGiven:0.00} = {r.Total:0.00}.");
                }
                case "discounts":
                {
                    var rows = await PaidTx().Where(t => t.DiscountId != null && t.Discount != null && t.Discount.Percentage > 0 && t.Discount.Percentage < 100)
                        .GroupBy(t => new { t.Discount!.Name, t.Discount.Percentage })
                        .Select(g => new { g.Key.Name, g.Key.Percentage, Net = g.Sum(x => x.TotalPrice), Cnt = g.Count() })
                        .ToListAsync(ct);
                    var list = rows.Select(x =>
                    {
                        var gross = x.Net / (1m - x.Percentage / 100m);
                        return new BreakdownRowDto($"{x.Name} ({x.Percentage}%)", Math.Round(gross - x.Net, 2), x.Cnt, $"on {x.Net:0.00} net");
                    }).OrderByDescending(x => x.Amount).ToList();
                    return new MetricBreakdownDto("discounts", "Discounts given — by discount", dash.Revenue.DiscountsGiven ?? 0m, list, CountLabel: "invoices");
                }
                case "opex":
                    return new MetricBreakdownDto("opex", "Operating Expenses by category", dash.OperatingExpenses.Total, ExpenseRows(dash.OperatingExpenses),
                        "Prorated to the period for entries that span several days.");
                case "net":
                    return new MetricBreakdownDto("net", "Net Income = Total Revenue − Operating Expenses",
                        dash.Revenue.Total - dash.OperatingExpenses.Total,
                        new List<BreakdownRowDto>
                        {
                            new("Total revenue", dash.Revenue.Total),
                            new("− Operating expenses", -dash.OperatingExpenses.Total),
                        }, "COGS is not subtracted here — see F&B Net and TCG Net.");
                case "gaming":
                    return new MetricBreakdownDto("gaming", "Gaming Revenue by game category", dash.Revenue.Gaming, await GamingByCategoryAsync(), CountLabel: "sessions");
                case "fnb":
                {
                    var (fnb, _) = await ItemRevenueByCategoryAsync();
                    return new MetricBreakdownDto("fnb", "F&B Revenue by category", dash.Revenue.Fnb, fnb, "Every item category whose type is not Retail.", CountLabel: "invoices");
                }
                case "tcg":
                {
                    var (_, tcg) = await ItemRevenueByCategoryAsync();
                    return new MetricBreakdownDto("tcg", "TCG / Retail Revenue by category", dash.Revenue.Tcg, tcg, "Item categories with type Retail (Pokemon, YuGiOh, Sleeves…).", CountLabel: "invoices");
                }
                case "fnbnet":
                    return new MetricBreakdownDto("fnbnet", "F&B Net = F&B Revenue − Ingredient COGS",
                        dash.Revenue.Fnb - (dash.Cogs.IngredientCogs ?? 0m),
                        new List<BreakdownRowDto>
                        {
                            new("F&B revenue", dash.Revenue.Fnb),
                            new("− Ingredient COGS (stock consumed)", -(dash.Cogs.IngredientCogs ?? 0m)),
                        }, "Open the Ingredient COGS breakdown to see which ingredients drive the cost.");
                case "foodcost":
                    return new MetricBreakdownDto("foodcost", "Food Cost % = Ingredient COGS ÷ Sales revenue",
                        dash.Cogs.FoodCostPercent ?? 0m,
                        new List<BreakdownRowDto>
                        {
                            new("Ingredient COGS", dash.Cogs.IngredientCogs ?? 0m),
                            new("÷ Sales revenue (gaming + F&B + TCG, excl. tickets)", dash.Revenue.Total - dash.Revenue.Events),
                        }, "Shown as a percentage on the tile.");
                case "inventory":
                {
                    var ings = await _ingredientRepo.Query().Where(i => i.IsActive && i.QuantityOnHand > 0 && i.BuyPricePerUnit != null)
                        .Select(i => new { i.Name, i.Unit, i.QuantityOnHand, Price = i.BuyPricePerUnit!.Value }).ToListAsync(ct);
                    var rows = ings.Select(i => new BreakdownRowDto(i.Name, Math.Round(i.QuantityOnHand * i.Price, 2), null, $"{i.QuantityOnHand:0.##} {i.Unit} × {i.Price:0.####}"))
                        .OrderByDescending(r => r.Amount).ToList();
                    return new MetricBreakdownDto("inventory", "Inventory Valuation by ingredient", rows.Sum(r => r.Amount), rows, "Quantity on hand × current buy price.");
                }
                case "tcgcogs":
                case "tcgstockbuy":
                case "tcgstocksell":
                case "tcgnet":
                {
                    var rep = await _itemReport.GetReportAsync(new ItemRevenueReportRequestDto
                    {
                        From = from.HasValue ? DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc) : null,
                        To = toExclusive.HasValue ? DateTime.SpecifyKind(toExclusive.Value, DateTimeKind.Utc) : null,
                    }, ct);
                    var tcgGroups = rep.Categories.Where(g => g.IsTcg).ToList();
                    if (metric == "tcgnet")
                        return new MetricBreakdownDto("tcgnet", "TCG Net = TCG Revenue − TCG COGS", rep.TcgRevenue - rep.TcgCogs,
                            tcgGroups.OrderByDescending(g => g.TotalGrossProfit)
                                .Select(g => new BreakdownRowDto(g.CategoryName, g.TotalGrossProfit, g.TotalUnitsSold, $"rev {g.TotalRevenue:0.00} − cogs {g.TotalCogs:0.00}")).ToList(),
                            CountLabel: "units");
                    if (metric == "tcgcogs")
                        return new MetricBreakdownDto("tcgcogs", "TCG Cost of Goods Sold by category", rep.TcgCogs,
                            tcgGroups.OrderByDescending(g => g.TotalCogs)
                                .Select(g => new BreakdownRowDto(g.CategoryName, g.TotalCogs, g.TotalUnitsSold + g.TotalUnitsGivenFree,
                                    g.TotalUnitsGivenFree > 0 ? $"{g.TotalUnitsGivenFree} given free in event kits" : null)).ToList(),
                            "Buy price × units sold (event-kit items included at cost).", CountLabel: "units");
                    if (metric == "tcgstockbuy")
                        return new MetricBreakdownDto("tcgstockbuy", "TCG Stock on hand — at cost", rep.TcgStockBuyValue,
                            tcgGroups.OrderByDescending(g => g.TotalStockBuyValue)
                                .Select(g => new BreakdownRowDto(g.CategoryName, g.TotalStockBuyValue, g.Items.Sum(i => i.StockOnHand))).ToList(),
                            CountLabel: "units on hand");
                    return new MetricBreakdownDto("tcgstocksell", "TCG Stock on hand — at retail price", rep.TcgStockSellValue,
                        tcgGroups.OrderByDescending(g => g.TotalStockSellValue)
                            .Select(g => new BreakdownRowDto(g.CategoryName, g.TotalStockSellValue, g.Items.Sum(i => i.StockOnHand))).ToList(),
                        CountLabel: "units on hand");
                }
                default:
                    throw new ArgumentException($"Unknown metric '{metric}'.");
            }
        }

        /// <summary>
        /// Where the ingredient COGS number comes from: per ingredient, the
        /// consumed quantity, the cost booked, the current buy price, and a
        /// flag when the two disagree (unit mismatch, stale price, double
        /// rebuild…). Also the top single movements so a runaway row is
        /// visible immediately.
        /// </summary>
        public async Task<IngredientCogsBreakdownDto> GetIngredientCogsBreakdownAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var toExclusive = to?.Date.AddDays(1);
            var q = _movementRepo.Query().Where(m => m.Type == "Consumption");
            if (from.HasValue) q = q.Where(m => m.CreatedOn >= from.Value.Date);
            if (toExclusive.HasValue) q = q.Where(m => m.CreatedOn < toExclusive.Value);

            var rows = await q
                .Select(m => new
                {
                    m.IngredientId,
                    IngredientName = m.Ingredient.Name,
                    IngredientUnit = m.Ingredient.Unit,
                    CurrentPrice = m.Ingredient.BuyPricePerUnit,
                    m.Quantity, m.UnitCost, m.TotalCost, m.ReferenceType, m.ReferenceId, m.CreatedOn, m.Id,
                })
                .ToListAsync(ct);

            var byIng = rows
                .GroupBy(r => new { r.IngredientId, r.IngredientName, r.IngredientUnit, r.CurrentPrice })
                .Select(g =>
                {
                    var qty = g.Sum(x => Math.Abs(x.Quantity));
                    var cost = g.Sum(x => x.TotalCost ?? 0m);
                    var avgUnit = qty > 0 ? cost / qty : 0m;
                    var cur = g.Key.CurrentPrice ?? 0m;
                    var expected = Math.Round(qty * cur, 2);
                    string? flag = null;
                    if (g.Key.CurrentPrice is null) flag = "No buy price on ingredient";
                    else if (cur > 0 && (avgUnit > cur * 1.5m || avgUnit < cur / 1.5m)) flag = $"Booked unit cost {avgUnit:0.####} vs current price {cur:0.####}/{g.Key.IngredientUnit} — unit or price mismatch";
                    return new IngredientCogsLineDto(
                        g.Key.IngredientId, g.Key.IngredientName, g.Key.IngredientUnit,
                        Math.Round(qty, 3), Math.Round(cost, 2), Math.Round(avgUnit, 4), g.Key.CurrentPrice, expected,
                        g.Count(), flag);
                })
                .OrderByDescending(l => l.TotalCost)
                .ToList();

            var top = rows
                .OrderByDescending(r => r.TotalCost ?? 0m)
                .Take(25)
                .Select(r => new IngredientCogsMovementDto(
                    r.Id, r.CreatedOn, r.IngredientName, r.IngredientUnit, Math.Round(Math.Abs(r.Quantity), 3),
                    r.UnitCost, r.TotalCost ?? 0m, r.ReferenceType, r.ReferenceId))
                .ToList();

            return new IngredientCogsBreakdownDto(
                From: from, To: to,
                Total: Math.Round(rows.Sum(r => r.TotalCost ?? 0m), 2),
                MovementCount: rows.Count,
                ExpectedAtCurrentPrices: byIng.Sum(l => l.ExpectedAtCurrentPrice),
                Lines: byIng,
                TopMovements: top);
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
