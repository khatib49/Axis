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
    /// <summary>Zone minimum-order validation, fee recording and the status workflow through the service.</summary>
    public class DeliveryOrderWorkflowTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly DeliveryService _svc;
        private readonly List<(List<OrderItemRequest> items, string comment, bool open, int? channelId)> _calls = new();

        public DeliveryOrderWorkflowTests()
        {
            _db = NewDb();
            SeedMenu(_db);
            _db.Add(new DeliveryZone { Id = 1, Name = "Hamra", Fee = 2.5m, MinOrder = 10m, EtaMinutes = 30, IsActive = true });
            _db.Add(new DeliveryZone { Id = 2, Name = "Closed zone", Fee = 1m, MinOrder = 0m, IsActive = false });
            _db.Add(new Driver { Id = 1, Name = "Ali", IsActive = true, UserId = 77 });
            _db.Add(new Driver { Id = 2, Name = "Gone", IsActive = false });
            _db.SaveChanges();
            _db.ChangeTracker.Clear();

            _svc = new DeliveryService(_db, TrxService(_db, _calls).Object, User().Object,
                new Mock<IIntegrationSettingsService>().Object, NullLogger<DeliveryService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        private static DeliveryOrderCreateDto Delivery(int? zoneId, params (int id, int qty)[] lines) =>
            new("Delivery", "Rami", "70123456", "Hamra street 5", null,
                lines.Select(l => new OrderItemRequest(l.id, l.qty)).ToList(), ZoneId: zoneId);

        [Fact]
        public async Task Order_below_the_zone_minimum_is_refused_and_no_invoice_is_created()
        {
            var r = await _svc.CreateAsync(Delivery(1, (2, 1)));   // one croissant = 3 < 10
            r.Success.Should().BeFalse();
            r.Error.Should().Be("MinOrder");
            _calls.Should().BeEmpty();
            _db.Set<DeliveryOrder>().Should().BeEmpty();
        }

        [Fact]
        public async Task Order_at_the_minimum_is_created_as_an_open_invoice_with_the_zone_fee()
        {
            var r = await _svc.CreateAsync(Delivery(1, (1, 2)));   // 2 lattes = 10 >= 10
            r.Success.Should().BeTrue(r.Message);
            var o = r.Data!;
            o.Status.Should().Be("Pending");
            o.ZoneName.Should().Be("Hamra");
            o.DeliveryFee.Should().Be(2.5m);
            o.Subtotal.Should().Be(10m);
            o.Total.Should().Be(12.5m);
            o.Items.Should().ContainSingle(l => l.ItemName == "Latte" && l.Quantity == 2);
            o.Events.Should().ContainSingle(e => e.ToStatus == "Pending");

            _calls.Should().ContainSingle();
            _calls[0].open.Should().BeTrue("delivery orders are open invoices closed later by the till");
            _calls[0].comment.Should().StartWith("Delivery: Rami/70123456/Hamra street 5");
        }

        [Fact]
        public async Task Inactive_zone_is_refused()
        {
            var r = await _svc.CreateAsync(Delivery(2, (3, 1)));
            r.Success.Should().BeFalse();
            r.Message.Should().Contain("not active");
        }

        [Fact]
        public async Task Takeaway_carries_no_fee_and_skips_the_driver_step()
        {
            var r = await _svc.CreateAsync(new DeliveryOrderCreateDto("Takeaway", "Maya", "03111222", null, null,
                new List<OrderItemRequest> { new(3, 1) }));
            r.Success.Should().BeTrue(r.Message);
            r.Data!.DeliveryFee.Should().Be(0m);
            _calls[0].comment.Should().StartWith("Takeaway: Maya/03111222");

            var id = r.Data.Id;
            (await _svc.AdvanceAsync(id, null, null)).Data!.Status.Should().Be("Preparing");
            (await _svc.AdvanceAsync(id, null, null)).Data!.Status.Should().Be("Ready");
            var done = await _svc.AdvanceAsync(id, null, null);
            done.Data!.Status.Should().Be("Delivered");
            done.Data.DeliveredOn.Should().NotBeNull();

            (await _svc.AssignDriverAsync(id, 1)).Success.Should().BeFalse("takeaway orders have no driver");
        }

        [Fact]
        public async Task Delivery_cannot_go_out_without_a_driver_and_illegal_jumps_are_refused()
        {
            var id = (await _svc.CreateAsync(Delivery(1, (3, 1)))).Data!.Id;

            var jump = await _svc.AdvanceAsync(id, "Delivered", null);
            jump.Success.Should().BeFalse();
            jump.Error.Should().Be("Transition");

            await _svc.AdvanceAsync(id, "Ready", null);
            var noDriver = await _svc.AdvanceAsync(id, "OutForDelivery", null);
            noDriver.Success.Should().BeFalse();
            noDriver.Message.Should().Contain("driver");

            (await _svc.AssignDriverAsync(id, 2)).Success.Should().BeFalse("inactive driver");
            var assigned = await _svc.AssignDriverAsync(id, 1);
            assigned.Success.Should().BeTrue();
            assigned.Data!.DriverName.Should().Be("Ali");
            assigned.Data.AssignedOn.Should().NotBeNull();

            var outNow = await _svc.AdvanceAsync(id, null, null);
            outNow.Data!.Status.Should().Be("OutForDelivery");
            outNow.Data.PickedUpOn.Should().NotBeNull();

            var delivered = await _svc.AdvanceAsync(id, null, null);
            delivered.Data!.Status.Should().Be("Delivered");

            (await _svc.CancelAsync(id, "late")).Success.Should().BeFalse("delivered orders cannot be cancelled");
            (await _svc.AdvanceAsync(id, null, null)).Success.Should().BeFalse("nothing after Delivered");

            var events = (await _svc.GetAsync(id))!.Events.Select(e => e.ToStatus).ToList();
            events.Should().Equal("Pending", "Ready", "Ready", "OutForDelivery", "Delivered"); // 2nd "Ready" = driver assignment note
        }

        [Fact]
        public async Task Cancel_records_the_reason_and_board_and_stats_reflect_it()
        {
            var a = (await _svc.CreateAsync(Delivery(1, (3, 1)))).Data!.Id;
            var b = (await _svc.CreateAsync(Delivery(1, (3, 2)))).Data!.Id;

            var c = await _svc.CancelAsync(b, "customer changed mind");
            c.Success.Should().BeTrue();
            c.Data!.Status.Should().Be("Cancelled");
            c.Data.CancelledReason.Should().Be("customer changed mind");

            var board = await _svc.GetBoardAsync();
            board.Columns.Single(col => col.Status == "Pending").Orders.Select(o => o.Id).Should().Equal(a);
            board.Columns.Single(col => col.Status == "Cancelled").Orders.Select(o => o.Id).Should().Equal(b);

            var stats = await _svc.GetTodayStatsAsync();
            stats.Total.Should().Be(2);
            stats.Cancelled.Should().Be(1);
            stats.Revenue.Should().Be(12m, "cancelled orders are excluded");
            stats.Fees.Should().Be(2.5m);
        }

        [Fact]
        public async Task Driver_sees_only_his_own_orders()
        {
            var mine = (await _svc.CreateAsync(Delivery(1, (3, 1)))).Data!.Id;
            var other = (await _svc.CreateAsync(Delivery(1, (3, 1)))).Data!.Id;
            await _svc.AssignDriverAsync(mine, 1);

            var driverSvc = new DeliveryService(_db, new Mock<ITransactionRecordService>().Object, User(userId: 77, name: "ali").Object,
                new Mock<IIntegrationSettingsService>().Object, NullLogger<DeliveryService>.Instance);
            (await driverSvc.MyOrdersAsync()).Select(o => o.Id).Should().Equal(mine);
            (await driverSvc.MyOrdersAsync()).Should().NotContain(o => o.Id == other);

            var stranger = new DeliveryService(_db, new Mock<ITransactionRecordService>().Object, User(userId: 5).Object,
                new Mock<IIntegrationSettingsService>().Object, NullLogger<DeliveryService>.Instance);
            (await stranger.MyOrdersAsync()).Should().BeEmpty();
        }
    }
}
