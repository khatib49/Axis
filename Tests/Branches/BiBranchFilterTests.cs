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
    /// <summary>BiReportService.BuildAsync / OwnerSummaryService honour branchId (null = all data, unchanged path).</summary>
    public class BiBranchFilterTests : IDisposable
    {
        private readonly ApplicationDbContext _db = TestDb.New();
        private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);

        public BiBranchFilterTests()
        {
            _db.AddRange(
                new Branch { Id = 1, Code = "HQ", Name = "Hamra", IsDefault = true },
                new Branch { Id = 2, Code = "JB", Name = "Jbeil" },
                new Category { Id = 1, Name = "Coffee", Type = "CoffeeShop" },
                new Item { Id = 1, Name = "Latte", Type = "CoffeeShop", CategoryId = 1, Price = 5 },
                new Item { Id = 2, Name = "Tea", Type = "CoffeeShop", CategoryId = 1, Price = 3 });
            _db.AddRange(
                Paid(1, 30, null), Paid(2, 10, 1), Paid(3, 50, 2), Paid(4, 70, 2, "bob"),
                Open(5, 25, null), Open(6, 5, 2));
            _db.AddRange(
                new TransactionItem { TransactionRecordId = 2, ItemId = 1, Quantity = 2 },
                new TransactionItem { TransactionRecordId = 3, ItemId = 2, Quantity = 1 });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private static TransactionRecord Paid(int id, decimal total, int? branchId, string by = "ann") => new()
        {
            Id = id, StatusId = 6, TotalPrice = total, BranchId = branchId, CreatedBy = by,
            CreatedOn = Day.AddHours(10), PaidOn = Day.AddHours(10),
        };

        private static TransactionRecord Open(int id, decimal total, int? branchId) => new()
        {
            Id = id, StatusId = 7, TotalPrice = total, BranchId = branchId, CreatedBy = "a", CreatedOn = Day.AddHours(12),
        };

        private static Mock<ITransactionRecordService> TxMock()
        {
            var m = new Mock<ITransactionRecordService>(MockBehavior.Loose);
            m.Setup(t => t.GetTotalsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, It.IsAny<CancellationToken>(), null))
                .ReturnsAsync(new PeriodTotalsDto(160, 4));
            m.Setup(t => t.GetItemSalesReportAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ItemSalesReportDto>());
            m.Setup(t => t.GetDailySalesAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<DailySalesDto>());
            return m;
        }

        private BiReportService Reports(Mock<ITransactionRecordService>? tx = null) => new((tx ?? TxMock()).Object, _db);

        private static string Summary(ReportPayload p, string key) => p.Summary.Single(kv => kv.Key == key).Value;

        [Fact]
        public async Task Null_branch_reports_on_all_data_through_the_existing_service()
        {
            var tx = TxMock();
            var p = await Reports(tx).BuildAsync("DailySales", Day, null);
            Summary(p, "Total sales").Should().Be("160.00");
            Summary(p, "Orders").Should().Be("4");
            p.Summary.Should().NotContain(kv => kv.Key == "Branch");
            tx.Verify(t => t.GetTotalsAsync(Day, Day, null, It.IsAny<CancellationToken>(), null), Times.Once);
        }

        [Fact]
        public async Task Daily_sales_for_a_branch_only_counts_that_branch()
        {
            var tx = TxMock();
            var p = await Reports(tx).BuildAsync("DailySales", Day, 2);
            Summary(p, "Branch").Should().Be("Jbeil");
            Summary(p, "Total sales").Should().Be("120.00");
            Summary(p, "Orders").Should().Be("2");
            Summary(p, "Average ticket").Should().Be("60.00");
            p.Rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { "Coffee", "1", "3.00" });
            tx.Verify(t => t.GetTotalsAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), null, It.IsAny<CancellationToken>(), null), Times.Never);
        }

        [Fact]
        public async Task Default_branch_also_owns_legacy_rows_without_a_branch()
        {
            var p = await Reports().BuildAsync("DailySales", Day, 1);
            Summary(p, "Total sales").Should().Be("40.00");
            Summary(p, "Orders").Should().Be("2");
            p.Rows.Should().ContainSingle().Which.Should().BeEquivalentTo(new[] { "Coffee", "2", "10.00" });
        }

        [Fact]
        public async Task Open_invoices_and_shift_summary_are_branch_scoped()
        {
            var open = await Reports().BuildAsync("OpenInvoices", Day, 2);
            Summary(open, "Open invoices").Should().Be("1");
            Summary(open, "Outstanding value").Should().Be("5.00");
            open.Rows.Should().ContainSingle().Which[0].Should().Be("6");

            var openAll = await Reports().BuildAsync("OpenInvoices", Day, null);
            Summary(openAll, "Open invoices").Should().Be("2");

            var shift = await Reports().BuildAsync("ShiftSummary", Day, 2);
            Summary(shift, "Paid transactions").Should().Be("2");
            Summary(shift, "Total collected").Should().Be("120.00");
            shift.Rows.Select(r => r[0]).Should().BeEquivalentTo(new[] { "ann", "bob" });
        }

        [Fact]
        public async Task Weekly_sales_for_a_branch_fills_seven_days()
        {
            var p = await Reports().BuildAsync("WeeklySales", Day, 2);
            p.Rows.Should().HaveCount(7);
            p.Rows.Last()[5].Should().Be("120.00");
            p.Rows.First()[5].Should().Be("0.00");
            Summary(p, "Total sales").Should().Be("120.00");
        }

        [Fact]
        public async Task Unknown_branch_yields_an_empty_report_not_all_data()
        {
            var p = await Reports().BuildAsync("DailySales", Day, 999);
            Summary(p, "Total sales").Should().Be("0.00");
            Summary(p, "Orders").Should().Be("0");
        }

        [Fact]
        public async Task Owner_summary_honours_branch_id()
        {
            var svc = new OwnerSummaryService(TxMock().Object, _db);
            var all = await svc.BuildAsync(Day);
            all.Today.Sales.Should().Be(160);
            all.OpenInvoicesCount.Should().Be(2);

            var jb = await svc.BuildAsync(Day, 2);
            jb.Today.Sales.Should().Be(120);
            jb.Today.Orders.Should().Be(2);
            jb.Today.AverageTicket.Should().Be(60);
            jb.OpenInvoicesCount.Should().Be(1);
            jb.OpenInvoicesValue.Should().Be(5);
            jb.TopItems.Should().ContainSingle(i => i.ItemName == "Tea");
            jb.Last14Days.Should().HaveCount(14);
            jb.Last14Days.Last().Total.Should().Be(120);
        }
    }
}
