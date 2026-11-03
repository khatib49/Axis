using Application.DTOs;
using Application.Services.AccountingPlus;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Tests.AccountingPlus
{
    public class VatTests
    {
        [Theory]
        [InlineData(111, 11, true, 11)]       // 111 incl. 11% → 11 VAT
        [InlineData(100, 11, false, 11)]      // 100 excl. → 11 on top
        [InlineData(222, 11, true, 22)]
        [InlineData(100, 0, true, 0)]
        [InlineData(0, 11, true, 0)]
        public void Vat_math_handles_inclusive_and_exclusive_prices(decimal amount, decimal rate, bool inclusive, decimal expected)
        {
            VatMath.Vat(amount, rate, inclusive).Should().Be(expected);
        }

        [Fact]
        public void Net_of_an_inclusive_price_plus_vat_equals_the_gross()
        {
            var vat = VatMath.Vat(123.45m, 11, true);
            (VatMath.Net(123.45m, 11, true) + vat).Should().Be(123.45m);
        }

        [Fact]
        public async Task Report_uses_default_sales_rate_paid_transactions_and_bill_tax_per_month()
        {
            using var h = new AccountingPlusTestHost();
            await h.Tax.CreateRateAsync(new TaxRateUpsertDto("Old", 10, false, true, "Sales"));
            await h.Tax.CreateRateAsync(new TaxRateUpsertDto("VAT 11%", 11, true, true, "Both"));
            (await h.Tax.ListRatesAsync(false)).Count(r => r.IsDefault).Should().Be(1);

            var aug = new DateTime(2026, 8, 5, 12, 0, 0, DateTimeKind.Utc);
            var sep = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
            h.Db.AddRange(
                new TransactionRecord { Id = 1, StatusId = 6, TotalPrice = 111, CreatedOn = aug, PaidOn = aug, CreatedBy = "t" },
                new TransactionRecord { Id = 2, StatusId = 6, TotalPrice = 222, CreatedOn = aug.AddDays(-40), PaidOn = sep, CreatedBy = "t" }, // paid in Sept
                new TransactionRecord { Id = 3, StatusId = 7, TotalPrice = 999, CreatedOn = aug, CreatedBy = "t" },                              // open → ignored
                new TransactionRecord { Id = 4, StatusId = 6, TotalPrice = 333, CreatedOn = sep, PaidOn = null, CreatedBy = "t" });             // no PaidOn → CreatedOn
            await h.Db.SaveChangesAsync();
            await h.Bills.CreateAsync(new SupplierBillCreateDto(1, "B1", aug, aug, 100, 11, AccountingPlusTestHost.Expense, null, null, null, "Open"));
            await h.Bills.CreateAsync(new SupplierBillCreateDto(1, "B2", sep, sep, 50, 5.5m, AccountingPlusTestHost.Expense, null, null, null, "Draft")); // draft ignored

            var r = await h.Tax.GetVatReportAsync(new DateTime(2026, 8, 1), new DateTime(2026, 9, 30), inclusive: true);
            r.Success.Should().BeTrue(r.Error);
            r.Data!.SalesRatePercent.Should().Be(11);
            r.Data.Rows.Should().HaveCount(2);

            var a = r.Data.Rows[0];
            a.Month.Should().Be(8);
            a.SalesGross.Should().Be(111);
            a.OutputVat.Should().Be(11);
            a.InputVat.Should().Be(11);
            a.NetPayable.Should().Be(0);

            var s = r.Data.Rows[1];
            s.SalesGross.Should().Be(555);
            s.OutputVat.Should().Be(55);
            s.InputVat.Should().Be(0);
            s.NetPayable.Should().Be(55);
            r.Data.TotalNetPayable.Should().Be(55);

            var ex = await h.Tax.GetVatReportAsync(new DateTime(2026, 8, 1), new DateTime(2026, 8, 31), inclusive: false);
            ex.Data!.Rows[0].OutputVat.Should().Be(12.21m);
        }
    }
}
