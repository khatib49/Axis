using Application.DTOs;
using Application.IServices;
using Application.Services.Ai;
using Application.Services.Phase4;
using Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Tests.OrderingChannels.OrderingChannelsTestSupport;

namespace Tests.Phase4
{
    public class UpsellServiceTests : IDisposable
    {
        private readonly Infrastructure.Persistence.ApplicationDbContext _db = NewDb();
        private readonly Mock<IIntegrationSettingsService> _settings = new();
        private readonly UpsellService _svc;

        public UpsellServiceTests()
        {
            SeedMenu(_db); // 1 Latte 5, 2 Croissant 3, 3 Burger 12 (all category 1, enabled)
            _db.Add(new Category { Id = 2, Name = "Drinks", Type = "item", ItemType = "Food" });
            _db.Add(new Item { Id = 4, Name = "Juice", Price = 4m, Quantity = 10, Type = "CoffeeShop", CategoryId = 2, StatusId = 1, SellOnline = false });
            _db.Add(new Item { Id = 5, Name = "Old cake", Price = 2m, Quantity = 10, Type = "CoffeeShop", CategoryId = 1, StatusId = 2 });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();
            _svc = new UpsellService(_db, User().Object, _settings.Object, new ClaudeApiClient(Mock.Of<IHttpClientFactory>()), NullLogger<UpsellService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public void ComputePairs_keeps_real_associations_with_correct_confidence_and_lift()
        {
            // 10 baskets: latte+croissant 4 times, latte alone 2, burger alone 4.
            var baskets = new List<int[]>();
            for (var i = 0; i < 4; i++) baskets.Add(new[] { 1, 2 });
            for (var i = 0; i < 2; i++) baskets.Add(new[] { 1 });
            for (var i = 0; i < 4; i++) baskets.Add(new[] { 3 });

            var pairs = UpsellService.ComputePairs(baskets, 3, 8);
            var latteToCroissant = pairs.Single(p => p.ItemId == 1 && p.SuggestItemId == 2);
            latteToCroissant.PairCount.Should().Be(4);
            latteToCroissant.Confidence.Should().Be(Math.Round(4m / 6m, 4));          // 4 of 6 latte baskets
            latteToCroissant.Lift.Should().Be(Math.Round((4m / 6m) / (4m / 10m), 4)); // croissant base rate 4/10
            pairs.Single(p => p.ItemId == 2 && p.SuggestItemId == 1).Confidence.Should().Be(1m);
            pairs.Should().NotContain(p => p.ItemId == 3 || p.SuggestItemId == 3);
        }

        [Fact]
        public void ComputePairs_drops_pairs_below_min_support_and_lift_one()
        {
            var baskets = new List<int[]> { new[] { 1, 2 }, new[] { 1, 2 }, new[] { 1 }, new[] { 2 } };
            UpsellService.ComputePairs(baskets, 3, 8).Should().BeEmpty();            // only 2 co-occurrences
            var everywhere = Enumerable.Range(0, 5).Select(_ => new[] { 1, 2 }).ToList(); // lift exactly 1
            UpsellService.ComputePairs(everywhere, 3, 8).Should().BeEmpty();
        }

        [Fact]
        public async Task Rebuild_learns_from_paid_invoices_only_and_reports_missing_ai_key()
        {
            var id = 100;
            void Tx(int status, params int[] items)
            {
                var t = new TransactionRecord { Id = ++id, StatusId = status, TotalPrice = 10, CreatedBy = "c", CreatedOn = DateTime.UtcNow.AddDays(-1) };
                _db.Add(t);
                foreach (var i in items) _db.Add(new TransactionItem { TransactionRecordId = t.Id, ItemId = i, Quantity = 1 });
            }
            for (var i = 0; i < 4; i++) Tx(6, 1, 2);
            for (var i = 0; i < 4; i++) Tx(6, 3);
            for (var i = 0; i < 10; i++) Tx(7, 3, 2); // open invoices must be ignored
            _db.SaveChanges();

            var r = await _svc.RebuildAsync(90, true);
            r.InvoicesScanned.Should().Be(8);
            r.PairsKept.Should().Be(2);
            r.AiNote.Should().Contain("Anthropic.ApiKey");
            (await _db.Set<UpsellPair>().AnyAsync(p => p.ItemId == 3)).Should().BeFalse();
        }

        [Fact]
        public async Task Suggest_puts_rules_first_then_learned_and_skips_cart_and_disabled_items()
        {
            _db.Add(new UpsellRule { TriggerItemId = 1, SuggestItemId = 3, Message = "Hungry? Add a burger", Priority = 5 });
            _db.Add(new UpsellRule { TriggerItemId = 1, SuggestItemId = 5, Priority = 9 }); // disabled item
            _db.Add(new UpsellPair { ItemId = 1, SuggestItemId = 2, PairCount = 10, Confidence = 0.6m, Lift = 2m, Pitch = "Pair it with a croissant" });
            _db.Add(new UpsellPair { ItemId = 1, SuggestItemId = 4, PairCount = 5, Confidence = 0.3m, Lift = 1.5m });
            _db.SaveChanges();

            var s = await _svc.SuggestAsync(new UpsellSuggestRequest(new() { 1 }, "Till", 3), publicOnly: false);
            s.Select(x => x.ItemId).Should().Equal(3, 2, 4);
            s[0].Source.Should().Be("Rule");
            s[0].Message.Should().Be("Hungry? Add a burger");
            s[1].Reason.Should().Be("60% of guests who order Latte add this");
            s[1].Message.Should().Be("Pair it with a croissant");

            var withCroissant = await _svc.SuggestAsync(new UpsellSuggestRequest(new() { 1, 2 }), false);
            withCroissant.Should().NotContain(x => x.ItemId == 2);
        }

        [Fact]
        public async Task Public_suggestions_only_offer_items_sold_online()
        {
            _db.Add(new UpsellPair { ItemId = 1, SuggestItemId = 4, PairCount = 5, Confidence = 0.9m, Lift = 3m });
            _db.SaveChanges();
            (await _svc.SuggestAsync(new UpsellSuggestRequest(new() { 1 }), publicOnly: true)).Should().BeEmpty();
            (await _svc.SuggestAsync(new UpsellSuggestRequest(new() { 1 }), publicOnly: false)).Should().ContainSingle(x => x.ItemId == 4);
        }

        [Fact]
        public async Task Category_rule_triggers_for_any_item_of_the_category()
        {
            _db.Add(new UpsellRule { TriggerCategoryId = 1, SuggestItemId = 4, Priority = 1 });
            _db.SaveChanges();
            (await _svc.SuggestAsync(new UpsellSuggestRequest(new() { 3 }), false)).Single().ItemId.Should().Be(4);
        }

        [Fact]
        public async Task Events_feed_the_acceptance_report_and_guests_cannot_log_till_events()
        {
            (await _svc.RecordEventAsync(new UpsellEventDto("Till", "Shown", 2), true)).Success.Should().BeFalse();
            (await _svc.RecordEventAsync(new UpsellEventDto("Nope", "Shown", 2), false)).Success.Should().BeFalse();
            for (var i = 0; i < 4; i++) (await _svc.RecordEventAsync(new UpsellEventDto("Till", "Shown", 2), false)).Success.Should().BeTrue();
            (await _svc.RecordEventAsync(new UpsellEventDto("Till", "Accepted", 2), false)).Success.Should().BeTrue();
            (await _svc.RecordEventAsync(new UpsellEventDto("Kiosk", "Shown", 3), true)).Success.Should().BeTrue();

            var st = await _svc.StatsAsync(30);
            st.Shown.Should().Be(5);
            st.Accepted.Should().Be(1);
            st.AcceptedRevenue.Should().Be(3m);
            st.ByChannel.Single(c => c.Channel == "Till").AcceptRate.Should().Be(0.25m);
            st.ByItem.Single(i => i.SuggestItemId == 2).Name.Should().Be("Croissant");
        }

        [Fact]
        public async Task Rules_need_a_trigger_and_cannot_suggest_the_trigger_itself()
        {
            (await _svc.CreateRuleAsync(new UpsellRuleUpsertDto(null, null, 2, null))).Success.Should().BeFalse();
            (await _svc.CreateRuleAsync(new UpsellRuleUpsertDto(2, null, 2, null))).Success.Should().BeFalse();
            var ok = await _svc.CreateRuleAsync(new UpsellRuleUpsertDto(1, null, 2, "Add a croissant?", 3));
            ok.Success.Should().BeTrue();
            ok.Data!.TriggerItemName.Should().Be("Latte");
            ok.Data.SuggestItemName.Should().Be("Croissant");
            (await _svc.DeleteRuleAsync(ok.Data.Id)).Should().BeTrue();
        }
    }
}
