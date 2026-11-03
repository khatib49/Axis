using Application.DTOs;
using Application.IServices;
using Application.Services.Bi;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Moq;
using Xunit;

namespace Tests.Branches
{
    public class OwnerSummaryTests : IDisposable
    {
        private readonly ApplicationDbContext _db = TestDb.New();
        private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

        public void Dispose() => _db.Dispose();

        private static Mock<ITransactionRecordService> Tx(decimal today, int todayOrders, decimal yesterday, int yesterdayOrders, decimal lastWeek, int lastWeekOrders)
        {
            var m = new Mock<ITransactionRecordService>(MockBehavior.Loose);
            m.Setup(t => t.GetTotalsAsync(Day, Day, null, It.IsAny<CancellationToken>(), null)).ReturnsAsync(new PeriodTotalsDto(today, todayOrders));
            m.Setup(t => t.GetTotalsAsync(Day.AddDays(-1), Day.AddDays(-1), null, It.IsAny<CancellationToken>(), null)).ReturnsAsync(new PeriodTotalsDto(yesterday, yesterdayOrders));
            m.Setup(t => t.GetTotalsAsync(Day.AddDays(-7), Day.AddDays(-7), null, It.IsAny<CancellationToken>(), null)).ReturnsAsync(new PeriodTotalsDto(lastWeek, lastWeekOrders));
            m.Setup(t => t.GetItemSalesReportAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ItemSalesReportDto>
                {
                    new() { ItemId = 1, ItemName = "Latte", CategoryName = "Coffee", TotalQuantity = 4, TotalAmount = 20 },
                    new() { ItemId = 2, ItemName = "Catan", CategoryName = "Games", TotalQuantity = 1, TotalAmount = 60 },
                });
            m.Setup(t => t.GetDailySalesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DailySalesDto> { new(Day, 80, 0, 80), new(Day.AddDays(-1), 100, 0, 100) });
            return m;
        }

        [Fact]
        public async Task Deltas_compare_today_with_yesterday_and_same_day_last_week()
        {
            var svc = new OwnerSummaryService(Tx(120, 6, 100, 5, 80, 8).Object, _db);
            var s = await svc.BuildAsync(Day);

            s.Today.Sales.Should().Be(120);
            s.Today.AverageTicket.Should().Be(20);
            s.SalesDeltaVsYesterdayPct.Should().Be(20.0m);
            s.SalesDeltaVsLastWeekPct.Should().Be(50.0m);
            s.OrdersDeltaVsYesterdayPct.Should().Be(20.0m);
            s.OrdersDeltaVsLastWeekPct.Should().Be(-25.0m);
        }

        [Fact]
        public async Task Delta_is_null_when_the_baseline_is_zero()
        {
            var svc = new OwnerSummaryService(Tx(50, 2, 0, 0, 25, 1).Object, _db);
            var s = await svc.BuildAsync(Day);
            s.SalesDeltaVsYesterdayPct.Should().BeNull();
            s.SalesDeltaVsLastWeekPct.Should().Be(100.0m);
        }

        [Fact]
        public async Task Top_items_series_open_invoices_and_low_stock_are_filled()
        {
            _db.AddRange(
                new TransactionRecord { Id = 1, StatusId = 7, TotalPrice = 15, CreatedBy = "a" },
                new TransactionRecord { Id = 2, StatusId = 7, TotalPrice = 5, CreatedBy = "a" },
                new TransactionRecord { Id = 3, StatusId = 6, TotalPrice = 50, CreatedBy = "a" },
                new Ingredient { Id = 1, Name = "Milk", QuantityOnHand = 1, ReorderLevel = 5 },
                new Ingredient { Id = 2, Name = "Beans", QuantityOnHand = 10, ReorderLevel = 5 },
                new Ingredient { Id = 3, Name = "Old", QuantityOnHand = 0, ReorderLevel = 5, IsActive = false });
            _db.SaveChanges();

            var s = await new OwnerSummaryService(Tx(10, 1, 10, 1, 10, 1).Object, _db).BuildAsync(Day);

            s.OpenInvoicesCount.Should().Be(2);
            s.OpenInvoicesValue.Should().Be(20);
            s.LowStockCount.Should().Be(1);
            s.TopItems.Select(i => i.ItemName).Should().ContainInOrder("Catan", "Latte");
            s.Last14Days.Should().HaveCount(14);
            s.Last14Days.Last().Date.Should().Be(Day);
            s.Last14Days.Last().Total.Should().Be(80);
            s.Last14Days.First().Total.Should().Be(0);
        }

        [Theory]
        [InlineData(110, 100, 10.0)]
        [InlineData(90, 100, -10.0)]
        [InlineData(0, 100, -100.0)]
        public void DeltaPct_rounds_to_one_decimal(decimal current, decimal previous, decimal expected) =>
            OwnerSummaryService.DeltaPct(current, previous).Should().Be(expected);
    }
}
