using Application.DTOs;
using Application.Services.TillControls;
using FluentAssertions;
using Xunit;

namespace Tests.TillControls
{
    public class CurrencyTests : IDisposable
    {
        private readonly TillFixture _f = new();
        public void Dispose() => _f.Dispose();

        [Fact]
        public async Task Seeds_usd_base_and_lbp_on_first_use()
        {
            var list = await _f.Currency.ListAsync();
            list.Should().HaveCount(2);
            list.Single(c => c.Code == "USD").IsBase.Should().BeTrue();
            list.Single(c => c.Code == "USD").CurrentRate.Should().Be(1m);
            list.Single(c => c.Code == "LBP").CurrentRate.Should().Be(89500m);
            list.Single(c => c.Code == "LBP").RoundingStep.Should().Be(1000m);
        }

        [Fact]
        public async Task Converts_with_latest_effective_rate_and_rounds_to_step()
        {
            await _f.Currency.EnsureSeededAsync();
            // 12.34 USD * 89500 = 1,104,430 → rounded to the nearest 1000 = 1,104,000
            var r = await _f.Currency.ConvertAsync(12.34m, "usd", "lbp");
            r.Success.Should().BeTrue();
            r.Data!.Result.Should().Be(1_104_000m);

            // Back to base: 1,104,000 / 89500 = 12.3352 → 12.34 (2dp, no step rounding on base)
            var back = await _f.Currency.ConvertAsync(1_104_000m, "LBP", "USD");
            back.Data!.Result.Should().Be(12.34m);

            // A new rate for today replaces the seeded one; a future-dated rate is ignored until then.
            (await _f.Currency.SetRateAsync(new SetRateDto("LBP", 90000m))).Success.Should().BeTrue();
            (await _f.Currency.SetRateAsync(new SetRateDto("LBP", 95000m, DateTime.UtcNow.AddDays(3)))).Success.Should().BeTrue();
            (await _f.Currency.GetRateAsync("LBP")).Should().Be(90000m);
            (await _f.Currency.ConvertAsync(1m, "USD", "LBP")).Data!.Result.Should().Be(90000m);

            (await _f.Currency.HistoryAsync("LBP")).Should().HaveCount(3);
        }

        [Theory]
        [InlineData(1_104_430, 1000, 0, 1_104_000)]
        [InlineData(1_104_500, 1000, 0, 1_105_000)]
        [InlineData(12.345, 0.01, 2, 12.35)]
        [InlineData(7.13, 0.25, 2, 7.25)]
        public void Rounding_to_step_is_nearest_half_up(decimal amount, decimal step, int dp, decimal expected)
            => CurrencyService.RoundToStep(amount, step, dp).Should().Be(expected);

        [Fact]
        public async Task Rejects_bad_rates_and_unknown_currencies()
        {
            (await _f.Currency.SetRateAsync(new SetRateDto("LBP", 0))).Success.Should().BeFalse();
            (await _f.Currency.SetRateAsync(new SetRateDto("USD", 2))).Success.Should().BeFalse();
            (await _f.Currency.ConvertAsync(1, "USD", "EUR")).Success.Should().BeFalse();
        }
    }
}
