using Application.DTOs;
using Application.Services.Promotions;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Promotions
{
    public class HappyHourWindowTests : IDisposable
    {
        private static HappyHourRule Rule(string days, string start, string end, decimal pct = 20m, Action<HappyHourRule>? tweak = null)
        {
            var r = new HappyHourRule
            {
                Id = 1, Name = "HH", DaysOfWeekCsv = days, DiscountPercent = pct, IsActive = true,
                StartTime = TimeOnly.Parse(start), EndTime = TimeOnly.Parse(end),
            };
            tweak?.Invoke(r);
            return r;
        }

        // 2026-11-02 is a Monday (ISO 1); 2026-11-06 is a Friday (5); 2026-11-07 Saturday (6)
        private static DateTime Local(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Unspecified);

        [Fact]
        public void Same_day_window_is_inclusive_start_exclusive_end()
        {
            var r = Rule("1,2,3,4,5", "17:00", "19:00");
            HappyHourEngine.IsActiveAt(r, Local(2, 16, 59)).Should().BeFalse();
            HappyHourEngine.IsActiveAt(r, Local(2, 17, 0)).Should().BeTrue();
            HappyHourEngine.IsActiveAt(r, Local(2, 18, 59)).Should().BeTrue();
            HappyHourEngine.IsActiveAt(r, Local(2, 19, 0)).Should().BeFalse();
        }

        [Fact]
        public void Day_of_week_filter_applies()
        {
            var weekdaysOnly = Rule("1,2,3,4,5", "17:00", "19:00");
            HappyHourEngine.IsActiveAt(weekdaysOnly, Local(7, 18, 0)).Should().BeFalse("Saturday");
            HappyHourEngine.IsActiveAt(Rule("", "17:00", "19:00"), Local(7, 18, 0)).Should().BeTrue("empty days = every day");
        }

        [Fact]
        public void Overnight_window_spans_midnight_and_keeps_the_starting_day()
        {
            var friNight = Rule("5", "22:00", "02:00");
            HappyHourEngine.IsActiveAt(friNight, Local(6, 21, 59)).Should().BeFalse();
            HappyHourEngine.IsActiveAt(friNight, Local(6, 22, 0)).Should().BeTrue("Fri 22:00");
            HappyHourEngine.IsActiveAt(friNight, Local(6, 23, 30)).Should().BeTrue();
            HappyHourEngine.IsActiveAt(friNight, Local(7, 1, 30)).Should().BeTrue("Sat 01:30 belongs to the Friday window");
            HappyHourEngine.IsActiveAt(friNight, Local(7, 2, 0)).Should().BeFalse("Sat 02:00 is the exclusive end");
            HappyHourEngine.IsActiveAt(friNight, Local(7, 23, 0)).Should().BeFalse("Saturday night is not in the day list");
            HappyHourEngine.IsActiveAt(friNight, Local(6, 12, 0)).Should().BeFalse("Friday noon");
        }

        [Fact]
        public void Inactive_or_zero_percent_rules_are_never_active()
        {
            HappyHourEngine.IsActiveAt(Rule("", "00:00", "23:59", tweak: r => r.IsActive = false), Local(2, 12)).Should().BeFalse();
            HappyHourEngine.IsActiveAt(Rule("", "00:00", "23:59", pct: 0), Local(2, 12)).Should().BeFalse();
        }

        [Fact]
        public void Best_rule_is_highest_priority_and_matching_respects_item_and_category_filters()
        {
            var all = Rule("", "00:00", "23:59", 10m, r => { r.Id = 1; r.Priority = 0; });
            var coffee = Rule("", "00:00", "23:59", 30m, r => { r.Id = 2; r.Priority = 5; r.CategoryIdsCsv = "1"; });
            var burger = Rule("", "00:00", "23:59", 50m, r => { r.Id = 3; r.Priority = 1; r.ItemIdsCsv = "2"; });

            HappyHourEngine.BestFor(new[] { all, coffee, burger }, itemId: 1, categoryId: 1)!.Id.Should().Be(2);
            HappyHourEngine.BestFor(new[] { all, coffee, burger }, itemId: 2, categoryId: 2)!.Id.Should().Be(3);
            HappyHourEngine.BestFor(new[] { all, coffee, burger }, itemId: 3, categoryId: 2)!.Id.Should().Be(1);
            HappyHourEngine.BestFor(new[] { coffee, burger }, itemId: 3, categoryId: 2).Should().BeNull();
            HappyHourEngine.DiscountedPrice(10m, 30m).Should().Be(7m);
        }

        [Fact]
        public void Venue_time_zone_resolves_to_beirut_or_falls_back_to_utc()
        {
            var tz = HappyHourEngine.VenueTimeZone();
            (tz.Id == "Asia/Beirut" || tz.Id == "Middle East Standard Time" || tz.Equals(TimeZoneInfo.Utc)).Should().BeTrue();
        }

        private readonly ApplicationDbContext _db = PromoDb.New();
        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Apply_discounts_matching_lines_once_per_invoice()
        {
            PromoDb.SeedCatalogAndInvoices(_db);
            // Always-on 50% off coffee (category 1). Invoice #10: 2 lattes (10) + burger (10) → 5 off.
            _db.Add(new HappyHourRule { Id = 1, Name = "Coffee hour", DaysOfWeekCsv = "", StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(0, 0), DiscountPercent = 50m, CategoryIdsCsv = "1", IsActive = true });
            await _db.SaveChangesAsync();

            var adjuster = new PromotionInvoiceAdjuster(_db, new FakeUser(), NullLogger<PromotionInvoiceAdjuster>.Instance);
            var auto = PromoAccounting.AutoApply(_db);
            var svc = new HappyHourService(_db, auto, new FakeUser(), NullLogger<HappyHourService>.Instance);

            (await svc.ActiveNowAsync()).Rules.Should().HaveCount(1);
            var preview = await svc.PricePreviewAsync(1);
            preview.Data!.DiscountedPrice.Should().Be(2.5m);
            (await svc.PricePreviewAsync(2)).Data!.Active.Should().BeFalse();

            var r = await svc.ApplyAsync(new HappyHourApplyDto(10));
            r.Success.Should().BeTrue(r.Message);
            r.Data!.DiscountAmount.Should().Be(5m);
            r.Data.MatchedLines.Should().Be(1);
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(15m);

            var again = await svc.ApplyAsync(new HappyHourApplyDto(10));
            again.Success.Should().BeFalse();
            again.Data!.AlreadyApplied.Should().BeTrue();
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(15m);

            (await svc.ApplyAsync(new HappyHourApplyDto(20))).Success.Should().BeFalse("closed invoice");
        }
    }
}
