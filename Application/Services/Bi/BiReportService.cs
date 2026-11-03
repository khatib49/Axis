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

        public Task<ReportPayload> BuildAsync(string reportType, DateTime day, int? branchId, CancellationToken ct = default)
        {
            // branchId is accepted for forward compatibility: transactions carry no branch yet,
            // so every builder reports on all data today.
            day = BusinessDate.ToDay(day);
            return (reportType ?? "").Trim().ToLowerInvariant() switch
            {
                "dailysales" => DailySalesAsync(day, ct),
                "weeklysales" => WeeklySalesAsync(day, ct),
                "lowstock" => LowStockAsync(ct),
                "openinvoices" => OpenInvoicesAsync(ct),
                "shiftsummary" => ShiftSummaryAsync(day, ct),
                _ => throw new ArgumentException($"Unknown report type '{reportType}'.", nameof(reportType)),
            };
        }

        private static string M(decimal v) => v.ToString("0.00");
        private static KeyValuePair<string, string> KV(string k, string v) => new(k, v);

        private async Task<ReportPayload> DailySalesAsync(DateTime day, CancellationToken ct)
        {
            var totals = await _tx.GetTotalsAsync(day, day, null, ct);
            var items = await _tx.GetItemSalesReportAsync(day, day, null, int.MaxValue, ct);
            var byCat = items
                .GroupBy(i => string.IsNullOrWhiteSpace(i.CategoryName) ? "(uncategorised)" : i.CategoryName)
                .Select(g => new { Category = g.Key, Qty = g.Sum(x => x.TotalQuantity), Amount = g.Sum(x => x.TotalAmount) })
                .OrderByDescending(x => x.Amount)
                .ToList();
            var avg = totals.OrdersCount == 0 ? 0 : Math.Round(totals.TotalAmount / totals.OrdersCount, 2);
            return new ReportPayload(
                $"Daily sales — {day:yyyy-MM-dd}", "DailySales", DateTime.UtcNow,
                new[] { KV("Total sales", M(totals.TotalAmount)), KV("Orders", totals.OrdersCount.ToString()), KV("Average ticket", M(avg)),
                        KV("Event tickets", M(totals.EventsAmount)) },
                new[] { "Category", "Quantity", "Amount" },
                byCat.Select(c => (IReadOnlyList<string>)new[] { c.Category, c.Qty.ToString(), M(c.Amount) }).ToList());
        }

        private async Task<ReportPayload> WeeklySalesAsync(DateTime day, CancellationToken ct)
        {
            var from = day.AddDays(-6);
            var daily = await _tx.GetDailySalesAsync(from, day, null, ct);
            var totals = await _tx.GetTotalsAsync(from, day, null, ct);
            var rows = new List<IReadOnlyList<string>>();
            for (var d = from; d <= day; d = d.AddDays(1))
            {
                var r = daily.FirstOrDefault(x => x.Date.Date == d.Date);
                rows.Add(new[] { d.ToString("yyyy-MM-dd"), d.DayOfWeek.ToString(), M(r?.ItemsTotal ?? 0), M(r?.GamesTotal ?? 0), M(r?.EventsTotal ?? 0), M(r?.GrandTotal ?? 0) });
            }
            return new ReportPayload(
                $"Weekly sales — {from:yyyy-MM-dd} to {day:yyyy-MM-dd}", "WeeklySales", DateTime.UtcNow,
                new[] { KV("Total sales", M(totals.TotalAmount)), KV("Orders", totals.OrdersCount.ToString()),
                        KV("Daily average", M(Math.Round(totals.TotalAmount / 7, 2))) },
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

        private async Task<ReportPayload> OpenInvoicesAsync(CancellationToken ct)
        {
            var open = await _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.StatusId == 7)
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
                "Open invoices", "OpenInvoices", now,
                new[] { KV("Open invoices", open.Count.ToString()), KV("Outstanding value", M(open.Sum(o => o.TotalPrice))) },
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
        private async Task<ReportPayload> ShiftSummaryAsync(DateTime day, CancellationToken ct)
        {
            var toExclusive = day.AddDays(1);
            var paid = await _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.StatusId == 6 && (t.PaidOn ?? t.CreatedOn) >= day && (t.PaidOn ?? t.CreatedOn) < toExclusive)
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
                $"Shift summary — {day:yyyy-MM-dd}", "ShiftSummary", DateTime.UtcNow,
                new[] { KV("Paid transactions", paid.Count.ToString()), KV("Total collected", M(paid.Sum(p => p.TotalPrice))),
                        KV("Cash", M(paid.Sum(p => p.TotalPrice - p.WalletPaidAmount))), KV("Wallet", M(paid.Sum(p => p.WalletPaidAmount))) },
                new[] { "Cashier", "Transactions", "F&B", "Games", "Cash", "Wallet", "Total" },
                groups.Select(g => (IReadOnlyList<string>)new[] { g.User, g.Count.ToString(), g.Fnb.ToString(), g.Games.ToString(), M(g.Total - g.Wallet), M(g.Wallet), M(g.Total) }).ToList());
        }

        private static string FormatAge(TimeSpan age) =>
            age.TotalHours >= 24 ? $"{(int)age.TotalDays}d {age.Hours}h" : $"{(int)age.TotalHours}h {age.Minutes}m";
    }
}
