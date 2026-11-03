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
    /// <summary>Kiosk: token authentication and takeaway order creation with an order number.</summary>
    public class KioskOrderTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly KioskService _svc;
        private readonly List<(List<OrderItemRequest> items, string comment, bool open, int? channelId)> _calls = new();

        public KioskOrderTests()
        {
            _db = NewDb();
            SeedMenu(_db);
            _db.Add(new KioskDevice { Id = 1, Name = "Front kiosk", Token = "tok-live", IsActive = true });
            _db.Add(new KioskDevice { Id = 2, Name = "Old kiosk", Token = "tok-dead", IsActive = false });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();

            var menuSvc = new Mock<IMenuService>();
            menuSvc.Setup(m => m.GetCategoriesMenuAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CategoryMenuDto> { new(1, "Coffee", "item") });
            menuSvc.Setup(m => m.GetItemsByCategoryAsync(1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ItemMenuDto> { new(1, "Latte", 5m, true, "CoffeeShop", null) });
            var user = User(userId: null, name: "system").Object;
            var delivery = new DeliveryService(_db, TrxService(_db, _calls).Object, user,
                new Mock<IIntegrationSettingsService>().Object, NullLogger<DeliveryService>.Instance);
            _svc = new KioskService(_db, delivery, new PublicMenuBuilder(menuSvc.Object, _db), user, NullLogger<KioskService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Unknown_or_inactive_token_is_unauthorized()
        {
            (await _svc.PlaceOrderAsync(null, new KioskOrderRequest(new List<OrderItemRequest> { new(1, 1) }))).Error.Should().Be("Unauthorized");
            (await _svc.PlaceOrderAsync("tok-dead", new KioskOrderRequest(new List<OrderItemRequest> { new(1, 1) }))).Error.Should().Be("Unauthorized");
            (await _svc.GetMenuAsync("wrong")).Error.Should().Be("Unauthorized");
            _calls.Should().BeEmpty();
        }

        [Fact]
        public async Task Valid_token_creates_a_takeaway_order_and_returns_its_number()
        {
            var r = await _svc.PlaceOrderAsync("tok-live", new KioskOrderRequest(new List<OrderItemRequest> { new(3, 2) }, Note: "no onions"));
            r.Success.Should().BeTrue(r.Message);
            r.Data!.OrderNumber.Should().BeGreaterThan(0);
            r.Data.Total.Should().Be(24m);
            r.Data.DeviceName.Should().Be("Front kiosk");

            var order = _db.Set<DeliveryOrder>().Single();
            order.Type.Should().Be("Takeaway");
            order.ExternalSource.Should().Be("kiosk");
            order.Status.Should().Be("Pending");
            order.DeliveryFee.Should().Be(0m);
            _calls.Should().ContainSingle();
            _calls[0].open.Should().BeTrue();
            _calls[0].comment.Should().Contain("Kiosk Front kiosk: no onions");

            _db.Set<KioskDevice>().Single(d => d.Id == 1).LastSeenOn.Should().NotBeNull();
        }

        [Fact]
        public async Task Rotating_a_token_invalidates_the_old_one()
        {
            var rotated = await _svc.RotateTokenAsync(1);
            rotated.Success.Should().BeTrue();
            rotated.Data!.Token.Should().NotBe("tok-live");
            (await _svc.GetMenuAsync("tok-live")).Error.Should().Be("Unauthorized");
            var menu = await _svc.GetMenuAsync(rotated.Data.Token);
            menu.Success.Should().BeTrue(menu.Message);
            menu.Data!.Categories.Should().ContainSingle(c => c.Name == "Coffee" && c.Items.Count == 1);
        }
    }
}
