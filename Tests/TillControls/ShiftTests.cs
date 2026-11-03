using Application.DTOs;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Tests.TillControls
{
    public class ShiftTests : IDisposable
    {
        private readonly TillFixture _f = new();
        public void Dispose() => _f.Dispose();

        [Fact]
        public async Task A_user_can_have_only_one_open_shift_per_register()
        {
            var first = await _f.Shifts.OpenAsync(new OpenShiftDto("Register 1", 100, null));
            first.Success.Should().BeTrue();

            var again = await _f.Shifts.OpenAsync(new OpenShiftDto("Register 1", 50, null));
            again.Success.Should().BeFalse();
            again.Error.Should().Contain("already have an open shift");

            // ...but another register, or another user on the same register, is fine.
            (await _f.Shifts.OpenAsync(new OpenShiftDto("Register 2", 50, null))).Success.Should().BeTrue();
            _f.User.UserId = 2; _f.User.UserName = "admin@axislb.com";
            (await _f.Shifts.OpenAsync(new OpenShiftDto("Register 1", 50, null))).Success.Should().BeTrue();

            // After closing, the register is free again for the first user.
            _f.User.UserId = 1; _f.User.UserName = "cashier@axislb.com";
            (await _f.Shifts.CloseAsync(new CloseShiftDto(100, null), first.Data!.Id)).Success.Should().BeTrue();
            (await _f.Shifts.OpenAsync(new OpenShiftDto("Register 1", 10, null))).Success.Should().BeTrue();
        }

        [Fact]
        public async Task Expected_cash_is_float_plus_cash_tenders_plus_payins_minus_payouts()
        {
            var open = await _f.Shifts.OpenAsync(new OpenShiftDto("R1", 100m, null));
            var shiftId = open.Data!.Id;

            _f.AddPaidTransaction(10, 40m);
            _f.AddPaidTransaction(11, 25m);
            // 40 USD cash, 25 USD by card (card must not count towards the drawer).
            (await _f.Tenders.AddAsync(new AddTendersDto(10, new() { new TenderLineDto("USD", 40m, "Cash") }))).Success.Should().BeTrue();
            (await _f.Tenders.AddAsync(new AddTendersDto(11, new() { new TenderLineDto("USD", 25m, "Card") }))).Success.Should().BeTrue();
            (await _f.Shifts.PayInAsync(new CashMovementCreateDto(20m, "Change from safe"))).Success.Should().BeTrue();
            (await _f.Shifts.PayOutAsync(new CashMovementCreateDto(15m, "Taxi for supplies"))).Success.Should().BeTrue();

            var x = await _f.Shifts.XReportAsync();
            x.Success.Should().BeTrue();
            x.Data!.ReportType.Should().Be("X");
            x.Data.ExpectedCash.Should().Be(100m + 40m + 20m - 15m);
            x.Data.CashTenderedInBase.Should().Be(40m);
            x.Data.TenderedTotalInBase.Should().Be(65m);
            x.Data.SalesByMethod.Should().ContainSingle(m => m.Method == "Card" && m.AmountInBase == 25m);
            x.Data.TransactionCount.Should().Be(2);
            x.Data.TransactionTotal.Should().Be(65m);
            x.Data.Variance.Should().BeNull();

            var z = await _f.Shifts.CloseAsync(new CloseShiftDto(140m, "short 5"), shiftId);
            z.Success.Should().BeTrue();
            z.Data!.ReportType.Should().Be("Z");
            z.Data.ExpectedCash.Should().Be(145m);
            z.Data.CountedCash.Should().Be(140m);
            z.Data.Variance.Should().Be(-5m);

            var stored = await _f.Shifts.GetAsync(shiftId);
            stored!.Status.Should().Be(ShiftStatuses.Closed);
            stored.ExpectedCash.Should().Be(145m);
            stored.Variance.Should().Be(-5m);

            // The Z report is frozen: a later pay-in cannot change it, and the shift is no longer "current".
            (await _f.Shifts.GetCurrentAsync()).Should().BeNull();
            var frozen = await _f.Shifts.ZReportAsync(shiftId);
            frozen!.ExpectedCash.Should().Be(145m);
            frozen.ReportType.Should().Be("Z");
        }

        [Fact]
        public async Task Report_only_counts_the_shift_users_paid_transactions_inside_the_window()
        {
            var before = DateTime.UtcNow.AddHours(-2);
            _f.AddPaidTransaction(1, 10m, paidOn: before);                   // before the shift
            await _f.Shifts.OpenAsync(new OpenShiftDto("R1", 0m, null));
            _f.AddPaidTransaction(2, 30m);                                   // mine, in window
            _f.AddPaidTransaction(3, 99m, createdBy: "someone@else.com");    // someone else's

            var x = await _f.Shifts.XReportAsync();
            x.Data!.TransactionCount.Should().Be(1);
            x.Data.TransactionTotal.Should().Be(30m);
        }
    }
}
