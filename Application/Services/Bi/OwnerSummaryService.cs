using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Bi
{
    public class OwnerSummaryService : IOwnerSummaryService
    {
        private readonly ITransactionRecordService _tx;
        private readonly ApplicationDbContext _db;

        public OwnerSummaryService(ITransactionRecordService tx, ApplicationDbContext db)
        {
            _tx = tx;
            _db = db;
        }

        /// <summary>Percentage change, 1 decimal; null when there is no baseline.</summary>
        public static decimal? DeltaPct(decimal current, decimal previous) =>
            previous == 0 ? null : Math.Round((current - previous) / previous * 100m, 1);

        private async Task<OwnerPeriodDto> PeriodAsync(DateTime day, BranchScopedSales.Scope? scope, CancellationToken ct)
        {
            if (scope is null)
            {
                var t = await _tx.GetTotalsAsync(day, day, null, ct);
                var avg = t.OrdersCount == 0 ? 0 : Math.Round(t.TotalAmount / t.OrdersCount, 2);
                return new OwnerPeriodDto(t.TotalAmount, t.OrdersCount, avg);
            }
            var b = await BranchScopedSales.TotalsAsync(_db, day, day.AddDays(1), scope, ct);
            return new OwnerPeriodDto(b.Amount, b.Orders, b.AverageTicket);
        }

        public Task<OwnerSummaryDto> BuildAsync(DateTime? day, CancellationToken ct = default) => BuildAsync(day, null, ct);

        /// <param name="branchId">Null → all branches (unchanged); otherwise transactions of that branch
        /// (the default branch also owns legacy rows without a branch). Low stock is never branch-scoped.</param>
        public async Task<OwnerSummaryDto> BuildAsync(DateTime? day, int? branchId, CancellationToken ct = default)
        {
            var today = BusinessDate.ToDay(day ?? DateTime.UtcNow);
            var yesterday = today.AddDays(-1);
            var lastWeek = today.AddDays(-7);
            var scope = await BranchScopedSales.ResolveAsync(_db, branchId, ct);

            var t = await PeriodAsync(today, scope, ct);
            var y = await PeriodAsync(yesterday, scope, ct);
            var w = await PeriodAsync(lastWeek, scope, ct);

            List<OwnerTopItemDto> top;
            var seriesFrom = today.AddDays(-13);
            var series = new List<OwnerSeriesPointDto>();
            if (scope is null)
            {
                top = (await _tx.GetItemSalesReportAsync(today, today, null, 5, ct) ?? new List<ItemSalesReportDto>())
                    .OrderByDescending(i => i.TotalAmount).Take(5)
                    .Select(i => new OwnerTopItemDto(i.ItemId, i.ItemName, i.CategoryName, i.TotalQuantity, i.TotalAmount)).ToList();
                var daily = await _tx.GetDailySalesAsync(seriesFrom, today, null, ct) ?? new List<DailySalesDto>();
                for (var d = seriesFrom; d <= today; d = d.AddDays(1))
                {
                    var r = daily.FirstOrDefault(x => x.Date.Date == d.Date);
                    series.Add(new OwnerSeriesPointDto(d, r?.GrandTotal ?? 0, r?.ItemsTotal ?? 0, r?.GamesTotal ?? 0, r?.EventsTotal ?? 0));
                }
            }
            else
            {
                top = (await BranchScopedSales.ItemsAsync(_db, today, today.AddDays(1), scope, ct))
                    .Take(5).Select(i => new OwnerTopItemDto(i.ItemId, i.ItemName, i.CategoryName, i.Quantity, i.Amount)).ToList();
                var daily = await BranchScopedSales.DailyAsync(_db, seriesFrom, today.AddDays(1), scope, ct);
                for (var d = seriesFrom; d <= today; d = d.AddDays(1))
                {
                    var r = daily.FirstOrDefault(x => x.Date.Date == d.Date);
                    series.Add(new OwnerSeriesPointDto(d, r?.Total ?? 0, r?.Items ?? 0, r?.Games ?? 0, 0));
                }
            }

            var open = await BranchScopedSales.Apply(_db.Set<TransactionRecord>().AsNoTracking().Where(x => x.StatusId == 7), scope)
                .Select(x => x.TotalPrice).ToListAsync(ct);
            var lowStock = await _db.Set<Ingredient>().AsNoTracking()
                .CountAsync(i => i.IsActive && i.ReorderLevel != null && i.QuantityOnHand < i.ReorderLevel, ct);

            return new OwnerSummaryDto(
                DateTime.UtcNow, today, t, y, w,
                DeltaPct(t.Sales, y.Sales), DeltaPct(t.Sales, w.Sales),
                DeltaPct(t.Orders, y.Orders), DeltaPct(t.Orders, w.Orders),
                open.Count, open.Sum(), lowStock, top, series);
        }
    }
}
