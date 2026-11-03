using Application.DTOs;
using FluentAssertions;
using Xunit;

namespace Tests.TillControls
{
    public class TenderTests : IDisposable
    {
        private readonly TillFixture _f = new();
        public void Dispose() => _f.Dispose();

        [Fact]
        public async Task Mixed_currency_tenders_sum_in_base_and_change_is_offered_in_every_currency()
        {
            _f.AddPaidTransaction(20, 15m);
            await _f.Shifts.OpenAsync(new OpenShiftDto("R1", 0m, null));

            // 10 USD cash + 500,000 LBP cash (= 5.59 USD @ 89500) → paid 15.59, change 0.59 USD ≈ 52,805 LBP → 53,000
            var r = await _f.Tenders.AddAsync(new AddTendersDto(20, new()
            {
                new TenderLineDto("USD", 10m, "Cash"),
                new TenderLineDto("LBP", 500_000m, "Cash"),
            }));

            r.Success.Should().BeTrue();
            var s = r.Data!;
            s.TotalPrice.Should().Be(15m);
            s.PaidInBase.Should().Be(15.59m);
            s.RemainingInBase.Should().Be(0m);
            s.ChangeInBase.Should().Be(0.59m);
            s.ChangeByCurrency.Should().Contain(c => c.CurrencyCode == "USD" && c.Amount == 0.59m);
            s.ChangeByCurrency.Should().Contain(c => c.CurrencyCode == "LBP" && c.Amount == 53_000m);
            s.Tenders.Should().HaveCount(2);
            s.Tenders.Single(t => t.CurrencyCode == "LBP").RateUsed.Should().Be(89500m);
            s.Tenders.Single(t => t.CurrencyCode == "LBP").AmountInBase.Should().Be(5.59m);
            s.Tenders.Should().OnlyContain(t => t.ShiftId != null, "tenders attach to the cashier's open shift");

            (await _f.Tenders.ListByShiftAsync(s.Tenders[0].ShiftId!.Value)).Should().HaveCount(2);
        }

        [Fact]
        public async Task Partial_payment_reports_the_remaining_amount()
        {
            _f.AddPaidTransaction(21, 30m);
            var r = await _f.Tenders.AddAsync(new AddTendersDto(21, new() { new TenderLineDto("USD", 12m, "Card", "VISA-1234") }));
            r.Data!.RemainingInBase.Should().Be(18m);
            r.Data.ChangeInBase.Should().Be(0m);
            r.Data.ChangeByCurrency.Should().OnlyContain(c => c.Amount == 0m);
            r.Data.Tenders.Single().ShiftId.Should().BeNull("no shift is open");
            r.Data.Tenders.Single().Reference.Should().Be("VISA-1234");
        }

        [Fact]
        public async Task Invalid_tenders_are_refused()
        {
            _f.AddPaidTransaction(22, 30m);
            (await _f.Tenders.AddAsync(new AddTendersDto(22, new()))).Success.Should().BeFalse();
            (await _f.Tenders.AddAsync(new AddTendersDto(22, new() { new TenderLineDto("USD", 0m) }))).Success.Should().BeFalse();
            (await _f.Tenders.AddAsync(new AddTendersDto(22, new() { new TenderLineDto("EUR", 5m) }))).Success.Should().BeFalse();
            (await _f.Tenders.AddAsync(new AddTendersDto(22, new() { new TenderLineDto("USD", 5m, "Crypto") }))).Success.Should().BeFalse();
            (await _f.Tenders.AddAsync(new AddTendersDto(999, new() { new TenderLineDto("USD", 5m) }))).Success.Should().BeFalse();
            (await _f.Tenders.SummaryAsync(999)).Success.Should().BeFalse();
        }

        [Fact]
        public async Task A_tender_can_be_removed_only_while_its_shift_is_open()
        {
            _f.AddPaidTransaction(23, 10m);
            var shift = await _f.Shifts.OpenAsync(new OpenShiftDto("R1", 0m, null));
            var r = await _f.Tenders.AddAsync(new AddTendersDto(23, new() { new TenderLineDto("USD", 10m) }));
            var tenderId = r.Data!.Tenders.Single().Id;

            await _f.Shifts.CloseAsync(new CloseShiftDto(10m, null), shift.Data!.Id);
            (await _f.Tenders.DeleteAsync(tenderId)).Success.Should().BeFalse();
        }
    }
}
