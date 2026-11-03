using Application.DTOs;
using Application.Services.AccountingPlus;
using Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.AccountingPlus
{
    public class DepreciationTests
    {
        private static readonly DateTime Acquired = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void Straight_line_schedule_is_even_and_sums_to_cost_minus_salvage()
        {
            var rows = DepreciationSchedule.Build(Acquired, 1000m, 100m, 36, "StraightLine");
            rows.Should().HaveCount(36);
            rows[0].Should().BeEquivalentTo(new { Year = 2026, Month = 1, Amount = 25.00m });
            rows[11].Year.Should().Be(2026); rows[11].Month.Should().Be(12);
            rows[12].Year.Should().Be(2027); rows[12].Month.Should().Be(1);
            rows.Sum(r => r.Amount).Should().Be(900m);
            rows.Last().NetBookValueAfter.Should().Be(100m);
        }

        [Fact]
        public void Straight_line_last_month_absorbs_rounding()
        {
            var rows = DepreciationSchedule.Build(Acquired, 1000m, 0m, 7, "StraightLine");
            rows.Should().HaveCount(7);
            rows.Take(6).Should().OnlyContain(r => r.Amount == 142.86m);
            rows.Last().Amount.Should().Be(1000m - 6 * 142.86m);
            rows.Sum(r => r.Amount).Should().Be(1000m);
        }

        [Fact]
        public void Declining_balance_front_loads_and_never_goes_below_salvage()
        {
            var rows = DepreciationSchedule.Build(Acquired, 1200m, 200m, 24, "DecliningBalance");
            rows.Should().NotBeEmpty();
            rows[0].Amount.Should().Be(100m); // 1200 × 2/24
            rows[1].Amount.Should().BeLessThan(rows[0].Amount);
            rows.Sum(r => r.Amount).Should().Be(1000m);
            rows.Should().OnlyContain(r => r.NetBookValueAfter >= 200m);
        }

        [Fact]
        public async Task Monthly_run_posts_one_journal_per_asset_and_is_idempotent()
        {
            using var h = new AccountingPlusTestHost();
            var a = (await h.Assets.CreateAsync(new FixedAssetUpsertDto("PS5 #1", "Gaming", Acquired, 1000, 100, 36, "StraightLine",
                AccountingPlusTestHost.AssetGl, AccountingPlusTestHost.AccumDep, AccountingPlusTestHost.DepExpense, null))).Data!;
            var later = (await h.Assets.CreateAsync(new FixedAssetUpsertDto("Later asset", "Gaming", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), 500, 0, 10, "StraightLine",
                AccountingPlusTestHost.AssetGl, AccountingPlusTestHost.AccumDep, AccountingPlusTestHost.DepExpense, null))).Data!;

            var first = await h.Assets.RunDepreciationAsync(2026, 2, null);
            first.Success.Should().BeTrue(first.Error);
            first.Data!.Posted.Should().Be(1, "the second asset was not yet acquired in Feb");
            first.Data.TotalAmount.Should().Be(25m);

            var second = await h.Assets.RunDepreciationAsync(2026, 2, null);
            second.Success.Should().BeTrue();
            second.Data!.Posted.Should().Be(0);
            second.Data.Skipped.Should().Be(1);

            var entries = await h.Db.Set<DepreciationEntry>().ToListAsync();
            entries.Should().ContainSingle(e => e.AssetId == a.Id && e.PeriodYear == 2026 && e.PeriodMonth == 2);
            var jes = await h.Db.Set<JournalEntry>().Include(j => j.Lines).Where(j => j.ReferenceType == "Depreciation").ToListAsync();
            jes.Should().HaveCount(1);
            jes[0].IsPosted.Should().BeTrue();
            jes[0].Lines.Single(l => l.AccountId == AccountingPlusTestHost.DepExpense).DebitAmount.Should().Be(25m);
            jes[0].Lines.Single(l => l.AccountId == AccountingPlusTestHost.AccumDep).CreditAmount.Should().Be(25m);

            var june = await h.Assets.RunDepreciationAsync(2026, 6, null);
            june.Data!.Posted.Should().Be(2);
            (await h.Assets.GetAsync(later.Id)).Data!.AccumulatedDepreciation.Should().Be(50m);

            var summary = await h.Assets.GetRegisterSummaryAsync();
            summary.TotalCost.Should().Be(1500m);
            summary.TotalAccumulatedDepreciation.Should().Be(100m);
            summary.TotalNetBookValue.Should().Be(1400m);
        }

        [Fact]
        public async Task Disposal_clears_cost_and_accumulated_depreciation_and_books_gain_or_loss()
        {
            using var h = new AccountingPlusTestHost();
            var a = (await h.Assets.CreateAsync(new FixedAssetUpsertDto("VR set", "Gaming", Acquired, 1000, 0, 10, "StraightLine",
                AccountingPlusTestHost.AssetGl, AccountingPlusTestHost.AccumDep, AccountingPlusTestHost.DepExpense, null))).Data!;
            await h.Assets.RunDepreciationAsync(2026, 1, a.Id);
            await h.Assets.RunDepreciationAsync(2026, 2, a.Id); // accumulated 200, NBV 800

            var r = await h.Assets.DisposeAsync(a.Id, new AssetDisposeDto(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), 700, AccountingPlusTestHost.Cash, AccountingPlusTestHost.GainLoss, null));
            r.Success.Should().BeTrue(r.Error);
            r.Data!.Status.Should().Be("Disposed");
            r.Data.NetBookValue.Should().Be(0);

            var je = await h.Db.Set<JournalEntry>().Include(j => j.Lines).SingleAsync(j => j.ReferenceType == "AssetDisposal");
            je.Lines.Single(l => l.AccountId == AccountingPlusTestHost.Cash).DebitAmount.Should().Be(700);
            je.Lines.Single(l => l.AccountId == AccountingPlusTestHost.AccumDep).DebitAmount.Should().Be(200);
            je.Lines.Single(l => l.AccountId == AccountingPlusTestHost.AssetGl).CreditAmount.Should().Be(1000);
            je.Lines.Single(l => l.AccountId == AccountingPlusTestHost.GainLoss).DebitAmount.Should().Be(100, "sold 100 below NBV = loss");
            je.Lines.Sum(l => l.DebitAmount).Should().Be(je.Lines.Sum(l => l.CreditAmount));

            (await h.Assets.RunDepreciationAsync(2026, 3, a.Id)).Data!.Posted.Should().Be(0, "disposed assets stop depreciating");
        }
    }
}
