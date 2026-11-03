using Application.DTOs;
using Application.IServices;
using Application.Services.OrderingChannels;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Tests.OrderingChannels.OrderingChannelsTestSupport;

namespace Tests.OrderingChannels
{
    /// <summary>Aggregator webhook: shared-secret check, idempotency on (source, externalRef), item + channel mapping.</summary>
    public class DeliveryWebhookTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly DeliveryService _svc;
        private readonly List<(List<OrderItemRequest> items, string comment, bool open, int? channelId)> _calls = new();

        public DeliveryWebhookTests()
        {
            _db = NewDb();
            SeedMenu(_db);
            _db.Add(new Channel { Id = 4, Name = "Toters", IsActive = true });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();

            var settings = new Mock<IIntegrationSettingsService>();
            settings.Setup(s => s.GetRawAsync("Delivery.WebhookSecret.toters", It.IsAny<CancellationToken>())).ReturnsAsync("s3cret");
            settings.Setup(s => s.GetRawAsync("Delivery.WebhookSecret.zomato", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

            _svc = new DeliveryService(_db, TrxService(_db, _calls).Object, User(userId: null, name: "system").Object,
                settings.Object, NullLogger<DeliveryService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        private static WebhookOrderPayload Payload(string externalRef = "T-1001") =>
            new(externalRef, "Nour", "71000000", "Verdun, bldg 3", new List<WebhookOrderItem>
            {
                new(1, null, 2),             // by id
                new(null, "croissant", 1),   // by sku = item name, case-insensitive
            }, Note: "no sugar", Fee: 3m);

        [Fact]
        public async Task Missing_or_wrong_secret_is_rejected_without_creating_anything()
        {
            (await _svc.IngestWebhookAsync("toters", null, Payload())).Error.Should().Be("Unauthorized");
            (await _svc.IngestWebhookAsync("Toters", "nope", Payload())).Error.Should().Be("Unauthorized");
            (await _svc.IngestWebhookAsync("zomato", "anything", Payload())).Error.Should().Be("NotConfigured");
            _calls.Should().BeEmpty();
        }

        [Fact]
        public async Task First_delivery_creates_the_order_mapped_to_the_channel_named_after_the_source()
        {
            var r = await _svc.IngestWebhookAsync("Toters", "s3cret", Payload());
            r.Success.Should().BeTrue(r.Message);
            r.Data!.Duplicate.Should().BeFalse();
            var o = r.Data.Order;
            o.ExternalSource.Should().Be("toters");
            o.ExternalRef.Should().Be("T-1001");
            o.ChannelId.Should().Be(4);
            o.DeliveryFee.Should().Be(3m);
            o.Subtotal.Should().Be(13m);   // 2 lattes + 1 croissant
            o.Total.Should().Be(16m);
            o.CustomerName.Should().Be("Nour");

            _calls.Should().ContainSingle();
            _calls[0].channelId.Should().Be(4);
            _calls[0].open.Should().BeTrue();
            _calls[0].items.Should().BeEquivalentTo(new[] { new OrderItemRequest(1, 2), new OrderItemRequest(2, 1) });
            _calls[0].comment.Should().Contain("toters #T-1001").And.Contain("no sugar");
        }

        [Fact]
        public async Task Replaying_the_same_externalRef_returns_the_original_order_and_creates_nothing_new()
        {
            var first = await _svc.IngestWebhookAsync("toters", "s3cret", Payload());
            var again = await _svc.IngestWebhookAsync("toters", "s3cret", Payload());
            var third = await _svc.IngestWebhookAsync("toters", "s3cret", Payload() with { CustomerName = "Someone else" });

            again.Success.Should().BeTrue();
            again.Data!.Duplicate.Should().BeTrue();
            again.Data.Order.Id.Should().Be(first.Data!.Order.Id);
            third.Data!.Duplicate.Should().BeTrue();
            third.Data.Order.CustomerName.Should().Be("Nour");

            _calls.Should().ContainSingle("CreateCoffeeShopOrder must run once per external ref");
            _db.Set<DeliveryOrder>().Count().Should().Be(1);
        }

        [Fact]
        public async Task Same_ref_from_a_different_source_is_a_different_order()
        {
            var settings = new Mock<IIntegrationSettingsService>();
            settings.Setup(s => s.GetRawAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("k");
            var svc = new DeliveryService(_db, TrxService(_db, _calls).Object, User().Object, settings.Object, NullLogger<DeliveryService>.Instance);

            var a = await svc.IngestWebhookAsync("toters", "k", Payload("R-7"));
            var b = await svc.IngestWebhookAsync("zomato", "k", Payload("R-7"));
            b.Data!.Duplicate.Should().BeFalse();
            b.Data.Order.Id.Should().NotBe(a.Data!.Order.Id);
            b.Data.Order.ChannelId.Should().BeNull("no channel named zomato exists");
        }

        [Fact]
        public async Task Unknown_sku_is_refused_before_any_invoice_is_created()
        {
            var bad = Payload() with { Items = new List<WebhookOrderItem> { new(null, "unicorn", 1) } };
            var r = await _svc.IngestWebhookAsync("toters", "s3cret", bad);
            r.Success.Should().BeFalse();
            r.Message.Should().Contain("unicorn");
            _calls.Should().BeEmpty();
        }
    }
}
