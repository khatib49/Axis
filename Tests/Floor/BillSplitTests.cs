using Application.DTOs;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Tests.Floor
{
    public class BillSplitTests : FloorTestBase
    {
        [Fact]
        public async Task Equal_split_sums_exactly_to_the_invoice_total_with_remainder_on_last()
        {
            SeedInvoice(10, 7, (1, 1)); // 10.00 split 3 ways → 3.33 + 3.33 + 3.34
            var r = await Billing.CreateEqualSplitsAsync(10, 3);
            r.Success.Should().BeTrue(r.Message);
            r.Data!.Splits.Should().HaveCount(3);
            r.Data.Splits.Select(s => s.Amount).Should().Equal(3.33m, 3.33m, 3.34m);
            r.Data.SplitTotal.Should().Be(10m);
            r.Data.Remaining.Should().Be(10m);
        }

        [Fact]
        public async Task Custom_splits_cannot_exceed_the_invoice_total()
        {
            SeedInvoice(11, 7, (1, 2)); // 20.00
            var ok = await Billing.CreateCustomSplitsAsync(11, new CustomSplitRequestDto(new() { new(12m), new(5m) }));
            ok.Success.Should().BeTrue(ok.Message);

            var tooMuch = await Billing.CreateCustomSplitsAsync(11, new CustomSplitRequestDto(new() { new(4m) })); // 12+5+4 = 21 > 20
            tooMuch.Success.Should().BeFalse();
            tooMuch.Message.Should().Contain("exceeds");

            var exact = await Billing.CreateCustomSplitsAsync(11, new CustomSplitRequestDto(new() { new(3m) })); // = 20
            exact.Success.Should().BeTrue(exact.Message);
            exact.Data!.SplitTotal.Should().Be(20m);
        }

        [Fact]
        public async Task Custom_split_rejects_non_positive_amounts()
        {
            SeedInvoice(12, 7, (1, 1));
            var r = await Billing.CreateCustomSplitsAsync(12, new CustomSplitRequestDto(new() { new(0m) }));
            r.Success.Should().BeFalse();
        }

        [Fact]
        public async Task Paying_a_split_reduces_remaining_and_paid_splits_cannot_be_deleted_or_repaid()
        {
            SeedInvoice(13, 7, (1, 2)); // 20.00
            var created = await Billing.CreateEqualSplitsAsync(13, 2);
            var first = created.Data!.Splits[0];

            var paid = await Billing.PaySplitAsync(first.Id, new PaySplitRequestDto("Card"));
            paid.Success.Should().BeTrue(paid.Message);
            paid.Data!.PaidTotal.Should().Be(10m);
            paid.Data.Remaining.Should().Be(10m);
            paid.Data.Splits[0].Method.Should().Be("Card");
            paid.Data.Splits[0].PaidBy.Should().Be("cashier@test");

            (await Billing.PaySplitAsync(first.Id, new PaySplitRequestDto())).Success.Should().BeFalse();
            (await Billing.DeleteSplitAsync(first.Id)).Success.Should().BeFalse();
            (await Billing.CreateEqualSplitsAsync(13, 4)).Success.Should().BeFalse("paid splits block a re-split");

            var second = paid.Data.Splits[1];
            var all = await Billing.PaySplitAsync(second.Id, new PaySplitRequestDto("Cash"));
            all.Data!.Remaining.Should().Be(0m);
        }

        [Fact]
        public async Task Tips_validate_amount_and_daily_report_groups_by_staff()
        {
            SeedInvoice(14, 6, (1, 1));
            (await Billing.AddTipAsync(14, new TipCreateDto(0m))).Success.Should().BeFalse();
            (await Billing.AddTipAsync(14, new TipCreateDto(5m, "Cash", 7, "Sara"))).Success.Should().BeTrue();
            (await Billing.AddTipAsync(14, new TipCreateDto(2.5m, "Card", 7, "Sara"))).Success.Should().BeTrue();
            (await Billing.AddTipAsync(14, new TipCreateDto(1m, "Cash", null, null))).Success.Should().BeTrue();

            var report = await Billing.DailyTipsReportAsync(DateTime.UtcNow);
            report.Total.Should().Be(8.5m);
            report.Count.Should().Be(3);
            report.ByStaff.Should().HaveCount(2);
            report.ByStaff.First().StaffName.Should().Be("Sara");
            report.ByStaff.First().Total.Should().Be(7.5m);
        }
    }
}
