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

        private async Task<OwnerPeriodDto> PeriodAsync(DateTime day, CancellationToken ct)
        {
            var t = await _tx.GetTotalsAsync(day, day, null, ct);
            var avg = t.OrdersCount == 0 ? 0 : Math.Round(t.TotalAmount / t.OrdersCount, 2);
            return new OwnerPeriodDto(t.TotalAmount, t.OrdersCount, avg);
        }

        public async Task<OwnerSummaryDto> BuildAsync(DateTime? day, CancellationToken ct = default)
        {
            var today = BusinessDate.ToDay(day ?? DateTime.UtcNow);
            var yesterday = today.AddDays(-1);
            var lastWeek = today.AddDays(-7);

            var t = await PeriodAsync(today, ct);
            var y = await PeriodAsync(yesterday, ct);
            var w = await PeriodAsync(lastWeek, ct);

            var top = (await _tx.GetItemSalesReportAsync(today, today, null, 5, ct) ?? new List<ItemSalesReportDto>())
                .OrderByDescending(i => i.TotalAmount).Take(5)
                .Select(i => new OwnerTopItemDto(i.ItemId, i.ItemName, i.CategoryName, i.TotalQuantity, i.TotalAmount)).ToList();

            var seriesFrom = today.AddDays(-13);
            var daily = await _tx.GetDailySalesAsync(seriesFrom, today, null, ct) ?? new List<DailySalesDto>();
            var series = new List<OwnerSeriesPointDto>();
            for (var d = seriesFrom; d <= today; d = d.AddDays(1))
            {
                var r = daily.FirstOrDefault(x => x.Date.Date == d.Date);
                series.Add(new OwnerSeriesPointDto(d, r?.GrandTotal ?? 0, r?.ItemsTotal ?? 0, r?.GamesTotal ?? 0, r?.EventsTotal ?? 0));
            }

            var open = await _db.Set<TransactionRecord>().AsNoTracking().Where(x => x.StatusId == 7)
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
