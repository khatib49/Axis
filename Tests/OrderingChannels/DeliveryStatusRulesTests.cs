using Application.Services.OrderingChannels;
using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Tests.OrderingChannels
{
    /// <summary>Allowed status transitions for delivery vs takeaway orders.</summary>
    public class DeliveryStatusRulesTests
    {
        [Theory]
        [InlineData("Delivery", "Pending", "Preparing", true)]
        [InlineData("Delivery", "Pending", "Ready", true)]
        [InlineData("Delivery", "Preparing", "Ready", true)]
        [InlineData("Delivery", "Ready", "OutForDelivery", true)]
        [InlineData("Delivery", "OutForDelivery", "Delivered", true)]
        [InlineData("Delivery", "Ready", "Delivered", false)]       // must go out first
        [InlineData("Delivery", "Pending", "Delivered", false)]
        [InlineData("Delivery", "Delivered", "Pending", false)]
        [InlineData("Delivery", "Cancelled", "Preparing", false)]
        [InlineData("Delivery", "Pending", "Cancelled", false)]     // cancel is its own action
        [InlineData("Takeaway", "Ready", "Delivered", true)]        // handed over at the counter
        [InlineData("Takeaway", "Ready", "OutForDelivery", false)]  // no driver step
        [InlineData("Takeaway", "Preparing", "Ready", true)]
        public void CanAdvance_follows_the_workflow(string type, string from, string to, bool expected)
            => DeliveryStatusRules.CanAdvance(type, from, to).Should().Be(expected);

        [Fact]
        public void DefaultNext_walks_the_happy_path_and_stops_at_delivered()
        {
            var s = DeliveryStatuses.Pending;
            var path = new List<string>();
            while (DeliveryStatusRules.DefaultNext(DeliveryOrderTypes.Delivery, s) is { } next) { path.Add(next); s = next; }
            path.Should().Equal("Preparing", "Ready", "OutForDelivery", "Delivered");

            s = DeliveryStatuses.Pending; path.Clear();
            while (DeliveryStatusRules.DefaultNext(DeliveryOrderTypes.Takeaway, s) is { } next) { path.Add(next); s = next; }
            path.Should().Equal("Preparing", "Ready", "Delivered");
        }

        [Theory]
        [InlineData("Pending", true)]
        [InlineData("OutForDelivery", true)]
        [InlineData("Delivered", false)]
        [InlineData("Cancelled", false)]
        public void CanCancel_only_before_a_terminal_status(string from, bool expected)
            => DeliveryStatusRules.CanCancel(from).Should().Be(expected);
    }
}
