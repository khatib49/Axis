using System.Net;
using System.Security.Cryptography;
using System.Text;
using Application.DTOs;
using Application.IServices;
using Application.Services.OrderingChannels;
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
    public class DeliveryPlatformServiceTests : IDisposable
    {
        private readonly Infrastructure.Persistence.ApplicationDbContext _db = NewDb();
        private readonly Mock<IIntegrationSettingsService> _settings = new();
        private readonly List<(List<OrderItemRequest> items, string comment, bool open, int? channelId)> _calls = new();
        private readonly FakeHandler _http = new();
        private readonly DeliveryService _delivery;
        private readonly DeliveryPlatformService _svc;

        public DeliveryPlatformServiceTests()
        {
            SeedMenu(_db); // 1 Latte 5, 2 Croissant 3, 3 Burger 12
            _settings.Setup(s => s.GetRawAsync("Delivery.WebhookSecret.toters", It.IsAny<CancellationToken>())).ReturnsAsync("s3cret");
            var user = User(userId: null, name: "system").Object;
            _delivery = new DeliveryService(_db, TrxService(_db, _calls).Object, user, _settings.Object, NullLogger<DeliveryService>.Instance);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(_http, disposeHandler: false));
            _svc = new DeliveryPlatformService(_db, _settings.Object, _delivery, factory.Object, user, NullLogger<DeliveryPlatformService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        private async Task<DeliveryPlatformDto> Connect(bool autoAccept = false, string? callback = "https://app.test/status", decimal markup = 10, bool mappedOnly = false)
        {
            var r = await _svc.CreateAsync(new DeliveryPlatformUpsertDto("Toters", "Toters", true, callback, markup, autoAccept, mappedOnly));
            r.Success.Should().BeTrue(r.Message);
            // Connecting happens "before" the test orders.
            var p = _db.Set<DeliveryPlatform>().First(); p.CreatedOn = DateTime.UtcNow.AddMinutes(-5); _db.SaveChanges();
            return r.Data!;
        }

        private static WebhookOrderPayload Order(string reference, string sku) =>
            new(reference, "Nour", "71000000", "Verdun", new List<WebhookOrderItem> { new(null, sku, 2) });

        [Fact]
        public async Task Validation_rejects_bad_slugs_reserved_names_markup_and_urls()
        {
            (await _svc.CreateAsync(new DeliveryPlatformUpsertDto("bad name", "X"))).Success.Should().BeFalse();
            (await _svc.CreateAsync(new DeliveryPlatformUpsertDto("kiosk", "X"))).Success.Should().BeFalse();
            (await _svc.CreateAsync(new DeliveryPlatformUpsertDto("toters", "X", PriceMarkupPercent: 150))).Success.Should().BeFalse();
            (await _svc.CreateAsync(new DeliveryPlatformUpsertDto("toters", "X", StatusCallbackUrl: "ftp://x"))).Success.Should().BeFalse();
            var ok = await Connect();
            ok.Source.Should().Be("toters");
            ok.SecretConfigured.Should().BeTrue();
            ok.WebhookPath.Should().Be("/api/delivery/webhook/toters");
            (await _svc.CreateAsync(new DeliveryPlatformUpsertDto("TOTERS", "Again"))).Error.Should().Be("Duplicate");
            (await _svc.UpdateAsync(ok.Id, new DeliveryPlatformUpsertDto("other", "Renamed"))).Success.Should().BeFalse();
        }

        [Fact]
        public async Task Webhook_resolves_app_item_ids_through_the_mapping_and_axis_ids()
        {
            var p = await Connect();
            (await _svc.UpsertMappingAsync(p.Id, new DeliveryItemMappingUpsertDto("TT-LATTE", 1))).Success.Should().BeTrue();
            var r = await _delivery.IngestWebhookAsync("toters", "s3cret", Order("A1", "TT-LATTE"));
            r.Success.Should().BeTrue(r.Message);
            _calls.Last().items.Single().ItemId.Should().Be(1);

            var r2 = await _delivery.IngestWebhookAsync("toters", "s3cret", Order("A2", "axis-3"));
            r2.Success.Should().BeTrue(r2.Message);
            _calls.Last().items.Single().ItemId.Should().Be(3);

            (await _delivery.IngestWebhookAsync("toters", "s3cret", Order("A3", "TT-UNKNOWN"))).Success.Should().BeFalse();
        }

        [Fact]
        public async Task Remapping_an_item_replaces_its_old_app_id()
        {
            var p = await Connect();
            await _svc.UpsertMappingAsync(p.Id, new DeliveryItemMappingUpsertDto("OLD", 2));
            await _svc.UpsertMappingAsync(p.Id, new DeliveryItemMappingUpsertDto("NEW", 2));
            (await _svc.ListMappingsAsync(p.Id)).Should().ContainSingle(m => m.ExternalItemId == "NEW" && m.ItemId == 2);
        }

        [Fact]
        public async Task Menu_feed_needs_the_secret_and_applies_markup_overrides_and_availability()
        {
            var p = await Connect(markup: 10);
            await _svc.UpsertMappingAsync(p.Id, new DeliveryItemMappingUpsertDto("TT-CR", 2, PriceOverride: 3.5m, IsAvailable: false));
            (await _svc.MenuFeedAsync("toters", "wrong", "https://api.test")).Error.Should().Be("Unauthorized");
            (await _svc.MenuFeedAsync("ubereats", "s3cret", "https://api.test")).Error.Should().Be("NotFound");

            var feed = (await _svc.MenuFeedAsync("toters", "s3cret", "https://api.test")).Data!;
            var items = feed.Categories.SelectMany(c => c.Items).ToList();
            items.Single(i => i.ItemId == 1).Should().Be(new DeliveryMenuFeedItemDto("axis-1", 1, "Latte", 5.5m, true, null));
            var cr = items.Single(i => i.ItemId == 2);
            cr.ExternalId.Should().Be("TT-CR");
            cr.Price.Should().Be(3.5m);
            cr.Available.Should().BeFalse();

            await _svc.UpdateAsync(p.Id, new DeliveryPlatformUpsertDto("toters", "Toters", true, p.StatusCallbackUrl, 10, false, MappedItemsOnly: true));
            (await _svc.MenuPreviewAsync(p.Id, "https://api.test")).Data!.Categories.SelectMany(c => c.Items).Should().ContainSingle(i => i.ItemId == 2);
        }

        [Fact]
        public async Task Auto_accept_moves_new_app_orders_to_preparing()
        {
            await Connect(autoAccept: true);
            var r = await _delivery.IngestWebhookAsync("toters", "s3cret", Order("B1", "axis-1"));
            await _svc.OnOrderIngestedAsync("toters", r.Data!.Order.Id);
            (await _db.Set<DeliveryOrder>().AsNoTracking().SingleAsync()).Status.Should().Be(DeliveryStatuses.Preparing);
        }

        [Fact]
        public async Task Status_changes_are_pushed_once_signed_and_retried_on_failure()
        {
            var p = await Connect();
            var order = (await _delivery.IngestWebhookAsync("toters", "s3cret", Order("C1", "axis-1"))).Data!.Order;
            await _delivery.AdvanceAsync(order.Id, DeliveryStatuses.Preparing, null);

            _http.Status = HttpStatusCode.InternalServerError;
            var run1 = await _svc.RunPushesAsync();
            run1.Enqueued.Should().Be(2);   // "received" for the new order, then "accepted"
            run1.Retrying.Should().Be(2);
            var pushes = await _svc.ListPushesAsync(p.Id, 10);
            pushes.Select(x => x.PlatformStatus).Should().BeEquivalentTo(new[] { "received", "accepted" });
            var push = pushes.First(x => x.PlatformStatus == "accepted");
            push.State.Should().Be("Pending");
            push.Attempts.Should().Be(1);
            push.LastHttpStatus.Should().Be(500);
            push.NextAttemptOn.Should().BeAfter(DateTime.UtcNow);

            // Not due yet: nothing is sent.
            (await _svc.RunPushesAsync()).Sent.Should().Be(0);
            foreach (var row in _db.Set<DeliveryPlatformPush>()) row.NextAttemptOn = DateTime.UtcNow.AddSeconds(-1);
            _db.SaveChanges();

            _http.Status = HttpStatusCode.OK;
            var run2 = await _svc.RunPushesAsync();
            run2.Sent.Should().Be(2);
            run2.Enqueued.Should().Be(0);

            var req = _http.Requests.Last();
            req.Body.Should().Contain("\"externalRef\":\"C1\"").And.Contain("\"status\":\"accepted\"");
            var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("s3cret"), Encoding.UTF8.GetBytes(req.Body))).ToLowerInvariant();
            req.Signature.Should().Be(expected);
            (await _svc.RunPushesAsync()).Should().Be(new DeliveryPushRunResultDto(0, 0, 0, 0));
        }

        [Fact]
        public async Task Pushes_give_up_after_max_attempts_and_can_be_retried()
        {
            var p = await Connect();
            var order = (await _delivery.IngestWebhookAsync("toters", "s3cret", Order("D1", "axis-1"))).Data!.Order;
            await _delivery.AdvanceAsync(order.Id, DeliveryStatuses.Preparing, null);
            _http.Status = HttpStatusCode.BadGateway;
            for (var i = 0; i < DeliveryPushStates.MaxAttempts; i++)
            {
                await _svc.RunPushesAsync();
                foreach (var r in _db.Set<DeliveryPlatformPush>()) r.NextAttemptOn = null;
                _db.SaveChanges();
            }
            var all = await _svc.ListPushesAsync(p.Id, 10);
            all.Should().HaveCount(2).And.OnlyContain(x => x.State == "Failed" && x.Attempts == DeliveryPushStates.MaxAttempts);
            var failed = all.First(x => x.PlatformStatus == "accepted");
            (await _svc.RetryPushAsync(p.Id, failed.Id)).Should().BeTrue();
            _http.Status = HttpStatusCode.OK;
            (await _svc.RunPushesAsync()).Sent.Should().Be(1);
        }

        [Fact]
        public async Task Orders_from_other_sources_and_apps_without_callback_are_not_pushed()
        {
            await Connect(callback: null);
            var order = (await _delivery.IngestWebhookAsync("toters", "s3cret", Order("E1", "axis-1"))).Data!.Order;
            await _delivery.AdvanceAsync(order.Id, DeliveryStatuses.Preparing, null);
            (await _svc.RunPushesAsync()).Enqueued.Should().Be(0);
            _http.Requests.Should().BeEmpty();
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
            public List<(string Url, string Body, string? Signature)> Requests { get; } = new();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
                Requests.Add((request.RequestUri!.ToString(), body, request.Headers.TryGetValues("X-Axis-Signature", out var v) ? v.First() : null));
                return new HttpResponseMessage(Status) { Content = new StringContent("{}") };
            }
        }
    }
}
