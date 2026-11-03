using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Bi
{
    public class BiReportService : IBiReportService
    {
        public static readonly string[] ReportTypes = { "DailySales", "WeeklySales", "LowStock", "OpenInvoices", "ShiftSummary" };

        private readonly ITransactionRecordService _tx;
        private readonly ApplicationDbContext _db;

        public BiReportService(ITransactionRecordService tx, ApplicationDbContext db)
        {
            _tx = tx;
            _db = db;
        }

        public string Render(ReportPayload payload, string format) => BiReportRenderer.Render(payload, format);

        public async Task<ReportPayload> BuildAsync(string reportType, DateTime day, int? branchId, CancellationToken ct = default)
        {
            // branchId = null → all data (unchanged behaviour, built on ITransactionRecordService).
            // branchId set → transactions filtered by BranchId; the default branch also owns legacy
            // rows without a branch (same attribution as the consolidated dashboard). LowStock is
            // not branch-scoped (ingredients carry no branch).
            day = BusinessDate.ToDay(day);
            var type = (reportType ?? "").Trim().ToLowerInvariant();
            if (type is not ("dailysales" or "weeklysales" or "lowstock" or "openinvoices" or "shiftsummary"))
                throw new ArgumentException($"Unknown report type '{reportType}'.", nameof(reportType));

            var scope = await BranchScopedSales.ResolveAsync(_db, branchId, ct);
            return type switch
            {
                "dailysales" => await DailySalesAsync(day, scope, ct),
                "weeklysales" => await WeeklySalesAsync(day, scope, ct),
                "lowstock" => await LowStockAsync(ct),
                "openinvoices" => await OpenInvoicesAsync(scope, ct),
                _ => await ShiftSummaryAsync(day, scope, ct),
            };
        }

        private static IEnumerable<KeyValuePair<string, string>> WithBranch(IEnumerable<KeyValuePair<string, string>> summary, BranchScopedSales.Scope? scope) =>
            scope is null ? summary : summary.Prepend(KV("Branch", scope.Name));

        private static string Suffix(BranchScopedSales.Scope? scope) => scope is null ? "" : $" — {scope.Name}";

        private static string M(decimal v) => v.ToString("0.00");
        private static KeyValuePair<string, string> KV(string k, string v) => new(k, v);

        private async Task<ReportPayload> DailySalesAsync(DateTime day, BranchScopedSales.Scope? scope, CancellationToken ct)
        {
            decimal total; int orders; decimal events;
            List<(string Category, int Qty, decimal Amount)> byCat;
            if (scope is null)
            {
                var totals = await _tx.GetTotalsAsync(day, day, null, ct);
                var items = await _tx.GetItemSalesReportAsync(day, day, null, int.MaxValue, ct);
                total = totals.TotalAmount; orders = totals.OrdersCount; events = totals.EventsAmount;
                byCat = items
                    .GroupBy(i => string.IsNullOrWhiteSpace(i.CategoryName) ? "(uncategorised)" : i.CategoryName)
                    .Select(g => (g.Key, g.Sum(x => x.TotalQuantity), g.Sum(x => x.TotalAmount)))
                    .ToList();
            }
            else
            {
                var totals = await BranchScopedSales.TotalsAsync(_db, day, day.AddDays(1), scope, ct);
                var items = await BranchScopedSales.ItemsAsync(_db, day, day.AddDays(1), scope, ct);
                total = totals.Amount; orders = totals.Orders; events = 0m;
                byCat = items
                    .GroupBy(i => string.IsNullOrWhiteSpace(i.CategoryName) ? "(uncategorised)" : i.CategoryName)
                    .Select(g => (g.Key, g.Sum(x => x.Quantity), g.Sum(x => x.Amount)))
                    .ToList();
            }
            byCat = byCat.OrderByDescending(x => x.Amount).ToList();
            var avg = orders == 0 ? 0 : Math.Round(total / orders, 2);
            return new ReportPayload(
                $"Daily sales — {day:yyyy-MM-dd}{Suffix(scope)}", "DailySales", DateTime.UtcNow,
                WithBranch(new[] { KV("Total sales", M(total)), KV("Orders", orders.ToString()), KV("Average ticket", M(avg)),
                        KV("Event tickets", M(events)) }, scope).ToList(),
                new[] { "Category", "Quantity", "Amount" },
                byCat.Select(c => (IReadOnlyList<string>)new[] { c.Category, c.Qty.ToString(), M(c.Amount) }).ToList());
        }

        private async Task<ReportPayload> WeeklySalesAsync(DateTime day, BranchScopedSales.Scope? scope, CancellationToken ct)
        {
            var from = day.AddDays(-6);
            var rows = new List<IReadOnlyList<string>>();
            decimal total; int orders;
            if (scope is null)
            {
                var daily = await _tx.GetDailySalesAsync(from, day, null, ct);
                var totals = await _tx.GetTotalsAsync(from, day, null, ct);
                total = totals.TotalAmount; orders = totals.OrdersCount;
                for (var d = from; d <= day; d = d.AddDays(1))
                {
                    var r = daily.FirstOrDefault(x => x.Date.Date == d.Date);
                    rows.Add(new[] { d.ToString("yyyy-MM-dd"), d.DayOfWeek.ToString(), M(r?.ItemsTotal ?? 0), M(r?.GamesTotal ?? 0), M(r?.EventsTotal ?? 0), M(r?.GrandTotal ?? 0) });
                }
            }
            else
            {
                var daily = await BranchScopedSales.DailyAsync(_db, from, day.AddDays(1), scope, ct);
                var totals = await BranchScopedSales.TotalsAsync(_db, from, day.AddDays(1), scope, ct);
                total = totals.Amount; orders = totals.Orders;
                for (var d = from; d <= day; d = d.AddDays(1))
                {
                    var r = daily.FirstOrDefault(x => x.Date.Date == d.Date);
                    rows.Add(new[] { d.ToString("yyyy-MM-dd"), d.DayOfWeek.ToString(), M(r?.Items ?? 0), M(r?.Games ?? 0), M(0), M(r?.Total ?? 0) });
                }
            }
            return new ReportPayload(
                $"Weekly sales — {from:yyyy-MM-dd} to {day:yyyy-MM-dd}{Suffix(scope)}", "WeeklySales", DateTime.UtcNow,
                WithBranch(new[] { KV("Total sales", M(total)), KV("Orders", orders.ToString()),
                        KV("Daily average", M(Math.Round(total / 7, 2))) }, scope).ToList(),
                new[] { "Date", "Day", "Items", "Games", "Events", "Total" },
                rows);
        }

        private async Task<ReportPayload> LowStockAsync(CancellationToken ct)
        {
            var low = await _db.Set<Ingredient>().AsNoTracking()
                .Where(i => i.IsActive && i.ReorderLevel != null && i.QuantityOnHand < i.ReorderLevel)
                .OrderBy(i => i.Name)
                .Select(i => new { i.Name, i.Unit, i.QuantityOnHand, ReorderLevel = i.ReorderLevel!.Value })
                .ToListAsync(ct);
            return new ReportPayload(
                "Low stock ingredients", "LowStock", DateTime.UtcNow,
                new[] { KV("Ingredients under reorder level", low.Count.ToString()) },
                new[] { "Ingredient", "Unit", "On hand", "Reorder level", "Shortfall" },
                low.Select(i => (IReadOnlyList<string>)new[] { i.Name, i.Unit, i.QuantityOnHand.ToString("0.###"), i.ReorderLevel.ToString("0.###"), (i.ReorderLevel - i.QuantityOnHand).ToString("0.###") }).ToList());
        }

        private async Task<ReportPayload> OpenInvoicesAsync(BranchScopedSales.Scope? scope, CancellationToken ct)
        {
            var open = await BranchScopedSales.Apply(_db.Set<TransactionRecord>().AsNoTracking().Where(t => t.StatusId == 7), scope)
                .OrderBy(t => t.CreatedOn)
                .Select(t => new
                {
                    t.Id, t.CreatedOn, t.CreatedBy, t.TotalPrice,
                    Client = t.User != null ? (t.User.DisplayName ?? t.User.UserName) : null,
                    Items = t.TransactionItems.Sum(ti => ti.Quantity),
                    IsGame = t.GameId != null,
                })
                .ToListAsync(ct);
            var now = DateTime.UtcNow;
            return new ReportPayload(
                $"Open invoices{Suffix(scope)}", "OpenInvoices", now,
                WithBranch(new[] { KV("Open invoices", open.Count.ToString()), KV("Outstanding value", M(open.Sum(o => o.TotalPrice))) }, scope).ToList(),
                new[] { "Invoice", "Opened (UTC)", "Age", "Opened by", "Client", "Type", "Items", "Total" },
                open.Select(o => (IReadOnlyList<string>)new[]
                {
                    o.Id.ToString(), o.CreatedOn.ToString("yyyy-MM-dd HH:mm"), FormatAge(now - o.CreatedOn), o.CreatedBy, o.Client ?? "",
                    o.IsGame ? "Game" : "F&B", o.Items.ToString(), M(o.TotalPrice),
                }).ToList());
        }

        /// <summary>
        /// Until a cashier-shift table exists (Till_CashierShifts), a "shift" is the set of
        /// transactions one user closed on the day. Swap the grouping key for the shift id later.
        /// </summary>
        private async Task<ReportPayload> ShiftSummaryAsync(DateTime day, BranchScopedSales.Scope? scope, CancellationToken ct)
        {
            var toExclusive = day.AddDays(1);
            var paid = await BranchScopedSales.Paid(_db, day, toExclusive, scope)
                .Select(t => new { t.CreatedBy, t.TotalPrice, t.WalletPaidAmount, IsGame = t.GameId != null })
                .ToListAsync(ct);
            var groups = paid.GroupBy(p => p.CreatedBy ?? "")
                .Select(g => new
                {
                    User = g.Key, Count = g.Count(), Total = g.Sum(x => x.TotalPrice), Wallet = g.Sum(x => x.WalletPaidAmount),
                    Games = g.Count(x => x.IsGame), Fnb = g.Count(x => !x.IsGame),
                })
                .OrderByDescending(g => g.Total).ToList();
            return new ReportPayload(
                $"Shift summary — {day:yyyy-MM-dd}{Suffix(scope)}", "ShiftSummary", DateTime.UtcNow,
                WithBranch(new[] { KV("Paid transactions", paid.Count.ToString()), KV("Total collected", M(paid.Sum(p => p.TotalPrice))),
                        KV("Cash", M(paid.Sum(p => p.TotalPrice - p.WalletPaidAmount))), KV("Wallet", M(paid.Sum(p => p.WalletPaidAmount))) }, scope).ToList(),
                new[] { "Cashier", "Transactions", "F&B", "Games", "Cash", "Wallet", "Total" },
                groups.Select(g => (IReadOnlyList<string>)new[] { g.User, g.Count.ToString(), g.Fnb.ToString(), g.Games.ToString(), M(g.Total - g.Wallet), M(g.Wallet), M(g.Total) }).ToList());
        }

        private static string FormatAge(TimeSpan age) =>
            age.TotalHours >= 24 ? $"{(int)age.TotalDays}d {age.Hours}h" : $"{(int)age.TotalHours}h {age.Minutes}m";
    }
}
