using Application.DTOs;
using Application.Services.Marketing;
using FluentAssertions;
using Xunit;

namespace Tests.Marketing
{
    /// <summary>Pure rule semantics: each rule alone, and AND across rules.</summary>
    public class SegmentRulesTests
    {
        private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

        private static AudienceFacts Facts(int? lastVisitDaysAgo = 5, decimal spendAll = 100, int visitsAll = 3, decimal spendPeriod = 40, int visitsPeriod = 1,
            int tickets = 0, string? email = "a@b.c", string? phone = "+96170000000") =>
            new(1, "Jane Doe", "Jane", email, phone, lastVisitDaysAgo is int d ? Now.AddDays(-d) : null, spendAll, visitsAll, spendPeriod, visitsPeriod, tickets);

        [Fact]
        public void Empty_rules_match_everyone() =>
            SegmentRules.Matches(new SegmentRulesDto(), Facts(lastVisitDaysAgo: null, spendAll: 0, visitsAll: 0, email: null, phone: null), Now).Should().BeTrue();

        [Fact]
        public void LastVisitWithinDays_requires_a_recent_visit()
        {
            var r = new SegmentRulesDto(LastVisitWithinDays: 30);
            SegmentRules.Matches(r, Facts(lastVisitDaysAgo: 5), Now).Should().BeTrue();
            SegmentRules.Matches(r, Facts(lastVisitDaysAgo: 45), Now).Should().BeFalse();
            SegmentRules.Matches(r, Facts(lastVisitDaysAgo: null), Now).Should().BeFalse("never visited");
        }

        [Fact]
        public void LastVisitOlderThanDays_matches_inactive_and_never_visited()
        {
            var r = new SegmentRulesDto(LastVisitOlderThanDays: 60);
            SegmentRules.Matches(r, Facts(lastVisitDaysAgo: 90), Now).Should().BeTrue();
            SegmentRules.Matches(r, Facts(lastVisitDaysAgo: null), Now).Should().BeTrue();
            SegmentRules.Matches(r, Facts(lastVisitDaysAgo: 10), Now).Should().BeFalse();
        }

        [Fact]
        public void MinSpend_uses_all_time_or_the_period_window()
        {
            SegmentRules.Matches(new SegmentRulesDto(MinSpend: 50), Facts(spendAll: 100, spendPeriod: 40), Now).Should().BeTrue();
            SegmentRules.Matches(new SegmentRulesDto(MinSpend: 50, SpendPeriodDays: 30), Facts(spendAll: 100, spendPeriod: 40), Now).Should().BeFalse();
            SegmentRules.Matches(new SegmentRulesDto(MinSpend: 150), Facts(spendAll: 100), Now).Should().BeFalse();
        }

        [Fact]
        public void MinVisits_counts_paid_visits()
        {
            SegmentRules.Matches(new SegmentRulesDto(MinVisits: 3), Facts(visitsAll: 3), Now).Should().BeTrue();
            SegmentRules.Matches(new SegmentRulesDto(MinVisits: 4), Facts(visitsAll: 3), Now).Should().BeFalse();
            SegmentRules.Matches(new SegmentRulesDto(MinVisits: 2, SpendPeriodDays: 30), Facts(visitsAll: 3, visitsPeriod: 1), Now).Should().BeFalse();
        }

        [Fact]
        public void LoyaltyTicketsMin_compares_current_month_tickets()
        {
            SegmentRules.Matches(new SegmentRulesDto(LoyaltyTicketsMin: 5), Facts(tickets: 5), Now).Should().BeTrue();
            SegmentRules.Matches(new SegmentRulesDto(LoyaltyTicketsMin: 5), Facts(tickets: 4), Now).Should().BeFalse();
        }

        [Fact]
        public void HasEmail_and_HasPhone_filter_on_contact_data()
        {
            SegmentRules.Matches(new SegmentRulesDto(HasEmail: true), Facts(email: null), Now).Should().BeFalse();
            SegmentRules.Matches(new SegmentRulesDto(HasEmail: true), Facts(email: "x@y.z"), Now).Should().BeTrue();
            SegmentRules.Matches(new SegmentRulesDto(HasEmail: false), Facts(email: "x@y.z"), Now).Should().BeFalse();
            SegmentRules.Matches(new SegmentRulesDto(HasPhone: true), Facts(phone: " "), Now).Should().BeFalse();
            SegmentRules.Matches(new SegmentRulesDto(HasPhone: true), Facts(phone: "+961"), Now).Should().BeTrue();
        }

        [Fact]
        public void Rules_are_ANDed()
        {
            var r = new SegmentRulesDto(LastVisitWithinDays: 30, MinSpend: 50, HasEmail: true);
            SegmentRules.Matches(r, Facts(lastVisitDaysAgo: 5, spendAll: 100, email: "a@b.c"), Now).Should().BeTrue();
            SegmentRules.Matches(r, Facts(lastVisitDaysAgo: 5, spendAll: 10, email: "a@b.c"), Now).Should().BeFalse("spend fails");
            SegmentRules.Matches(r, Facts(lastVisitDaysAgo: 5, spendAll: 100, email: null), Now).Should().BeFalse("email fails");
            SegmentRules.Matches(r, Facts(lastVisitDaysAgo: 60, spendAll: 100, email: "a@b.c"), Now).Should().BeFalse("visit fails");
        }

        [Fact]
        public void Parse_is_case_insensitive_and_tolerates_garbage()
        {
            var r = SegmentRules.Parse("{\"lastVisitWithinDays\":30,\"hasEmail\":true,\"MinSpend\":12.5}");
            r.LastVisitWithinDays.Should().Be(30); r.HasEmail.Should().BeTrue(); r.MinSpend.Should().Be(12.5m);
            SegmentRules.Parse("not json").Should().BeEquivalentTo(new SegmentRulesDto());
            SegmentRules.Parse(null).Should().BeEquivalentTo(new SegmentRulesDto());
            SegmentRules.Clean(new SegmentRulesDto(LastVisitWithinDays: -3, MinSpend: 0)).Should().BeEquivalentTo(new SegmentRulesDto());
        }
    }

    /// <summary>Audience loading from the DB: client role, paid status, PaidOn ?? CreatedOn, preview shape.</summary>
    public class SegmentAudienceTests
    {
        [Fact]
        public async Task Audience_only_contains_client_users_with_paid_sales_in_window()
        {
            using var h = new MarketingHarness();
            var recent = h.AddClient("recent@x.com", "+1", "Rina");
            var old = h.AddClient("old@x.com", "+2", "Omar");
            var openOnly = h.AddClient("open@x.com", "+3", "Oli");
            var staff = h.AddClient("staff@x.com", "+4", "Sam", client: false);
            h.AddSale(recent, 3, 25m);
            h.AddSale(old, 90, 500m);
            h.AddSale(openOnly, 1, 50m, status: 7);           // open invoice: not a visit
            h.AddSale(staff, 1, 50m);

            var rows = await h.Segments.BuildAudienceAsync(new SegmentRulesDto(LastVisitWithinDays: 30));
            rows.Select(r => r.UserId).Should().BeEquivalentTo(new[] { recent });
            rows[0].Spend.Should().Be(25m);
            rows[0].Email.Should().Be("recent@x.com");

            var inactive = await h.Segments.BuildAudienceAsync(new SegmentRulesDto(LastVisitOlderThanDays: 60));
            inactive.Select(r => r.UserId).Should().BeEquivalentTo(new[] { old, openOnly });

            var everyone = await h.Segments.PreviewRulesAsync(new SegmentRulesDto());
            everyone.Count.Should().Be(3, "staff is not a client");
            everyone.Rows.Should().HaveCount(3);
        }

        [Fact]
        public async Task Visit_date_falls_back_to_CreatedOn_when_PaidOn_is_null_and_BranchId_scopes_sales()
        {
            using var h = new MarketingHarness();
            var u = h.AddClient("a@x.com", "+1");
            h.AddSale(u, 10, 30m, viaPaidOn: false, branchId: 2);
            (await h.Segments.BuildAudienceAsync(new SegmentRulesDto(LastVisitWithinDays: 15))).Should().HaveCount(1);
            (await h.Segments.BuildAudienceAsync(new SegmentRulesDto(LastVisitWithinDays: 15, BranchId: 1))).Should().BeEmpty();
            (await h.Segments.BuildAudienceAsync(new SegmentRulesDto(LastVisitWithinDays: 15, BranchId: 2))).Should().HaveCount(1);
        }

        [Fact]
        public async Task Segment_crud_round_trips_rules()
        {
            using var h = new MarketingHarness();
            var created = await h.Segments.CreateAsync(new SegmentCreateDto("VIP", new SegmentRulesDto(MinSpend: 100, HasEmail: true)));
            created.Success.Should().BeTrue();
            var got = await h.Segments.GetAsync(created.Data!.Id);
            got!.Rules.MinSpend.Should().Be(100); got.Rules.HasEmail.Should().BeTrue(); got.Rules.MinVisits.Should().BeNull();
            (await h.Segments.CreateAsync(new SegmentCreateDto(" ", null))).Success.Should().BeFalse();
        }
    }
}
