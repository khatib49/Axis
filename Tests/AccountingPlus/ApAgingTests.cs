using Application.DTOs;
using Application.Services.AccountingPlus;
using FluentAssertions;
using Xunit;

namespace Tests.AccountingPlus
{
    public class ApAgingTests : IDisposable
    {
        private readonly AccountingPlusTestHost _h = new();
        private static readonly DateTime AsOf = new(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc);

        public void Dispose() => _h.Dispose();

        [Theory]
        [InlineData(0, AgingBuckets.Current)]
        [InlineData(-5, AgingBuckets.Current)]   // not yet due
        [InlineData(1, AgingBuckets.Days1To30)]
        [InlineData(30, AgingBuckets.Days1To30)]
        [InlineData(31, AgingBuckets.Days31To60)]
        [InlineData(60, AgingBuckets.Days31To60)]
        [InlineData(61, AgingBuckets.Days61To90)]
        [InlineData(90, AgingBuckets.Days61To90)]
        [InlineData(91, AgingBuckets.Over90)]
        [InlineData(400, AgingBuckets.Over90)]
        public void Bucket_boundaries_are_inclusive_on_the_upper_edge(int daysOverdue, string expected)
        {
            AgingBuckets.Bucket(AsOf.AddDays(-daysOverdue), AsOf).Should().Be(expected);
        }

        [Fact]
        public async Task Ap_aging_groups_outstanding_by_supplier_and_due_bucket()
        {
            // supplier 1: 100 due in 10 days (current), 200 due 45 days ago (31-60), fully paid bill ignored
            await Create(1, "A1", AsOf.AddDays(-20), AsOf.AddDays(10), 100);
            await Create(1, "A2", AsOf.AddDays(-60), AsOf.AddDays(-45), 200);
            var paid = await Create(1, "A3", AsOf.AddDays(-60), AsOf.AddDays(-45), 50);
            (await _h.Bills.AddPaymentAsync(paid.Id, new SupplierBillPaymentCreateDto(AsOf.AddDays(-1), 50, "Cash", null, AccountingPlusTestHost.Cash))).Success.Should().BeTrue();
            // supplier 2: 300 bill with 100 paid, due 100 days ago → 200 in 90+
            var partial = await Create(2, "B1", AsOf.AddDays(-120), AsOf.AddDays(-100), 300);
            (await _h.Bills.AddPaymentAsync(partial.Id, new SupplierBillPaymentCreateDto(AsOf.AddDays(-1), 100, "Cash", null, AccountingPlusTestHost.Cash))).Success.Should().BeTrue();
            // draft bills never age
            await _h.Bills.CreateAsync(new SupplierBillCreateDto(2, "DRAFT", AsOf, AsOf, 999, 0, AccountingPlusTestHost.Expense, null, null, null, "Draft"));

            var report = await _h.Bills.GetApAgingAsync(AsOf);

            report.Rows.Should().HaveCount(2);
            var s1 = report.Rows.Single(r => r.SupplierId == 1);
            s1.SupplierName.Should().Be("Fresh Farms");
            s1.OpenBills.Should().Be(2);
            s1.Buckets.Current.Should().Be(100);
            s1.Buckets.Days31To60.Should().Be(200);
            s1.Buckets.Total.Should().Be(300);

            var s2 = report.Rows.Single(r => r.SupplierId == 2);
            s2.Buckets.Over90.Should().Be(200);
            s2.Buckets.Total.Should().Be(200);

            report.Totals.Total.Should().Be(500);
            report.Totals.Current.Should().Be(100);
            report.Totals.Days31To60.Should().Be(200);
            report.Totals.Over90.Should().Be(200);
        }

        private async Task<SupplierBillDto> Create(int supplierId, string number, DateTime billDate, DateTime due, decimal subtotal)
        {
            var r = await _h.Bills.CreateAsync(new SupplierBillCreateDto(supplierId, number, billDate, due, subtotal, 0, AccountingPlusTestHost.Expense, null, null, null, "Open"));
            r.Success.Should().BeTrue(r.Error);
            return r.Data!;
        }
    }
}
