using Application.DTOs;
using Application.Services.Payments2;
using Application.Services.TillControls;
using Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Tests.Payments2
{
    public class DisplayStateTests
    {
        [Theory]
        [InlineData(1.00, 89500, 1000, 90000)]     // 89,500 rounds up to the next 1,000 step
        [InlineData(2.50, 89500, 1000, 224000)]    // 223,750 → 224,000
        [InlineData(0.30, 89500, 1000, 27000)]     // 26,850 → 27,000
        [InlineData(10.00, 89500, 5000, 895000)]   // already on a 5,000 step
        [InlineData(10.00, 0, 1000, 0)]            // no rate known → 0
        public void Totals_in_lbp_follow_rate_and_rounding_step(double usd, double rate, double step, double expectedLbp)
        {
            var cart = new[] { new DisplayCartLineDto("Coffee", 1, (decimal)usd) };
            var (totalUsd, totalLbp) = DisplayStateService.ComputeTotals(cart, (decimal)rate, (decimal)step, 0);
            totalUsd.Should().Be((decimal)usd);
            totalLbp.Should().Be((decimal)expectedLbp);
        }

        [Fact]
        public void Totals_sum_lines_with_quantity_and_round_usd_to_cents()
        {
            var cart = new[] { new DisplayCartLineDto("A", 3, 1.333m), new DisplayCartLineDto("B", 2, 2.5m) };
            var (usd, lbp) = DisplayStateService.ComputeTotals(cart, 89500m, 1000m, 0);
            usd.Should().Be(9.00m);          // 3.999 + 5 = 8.999 → 9.00
            lbp.Should().Be(806000m);        // 805,500 → 806,000
        }

        [Fact]
        public async Task Push_creates_display_recomputes_totals_and_view_returns_cart()
        {
            using var f = new PaymentsFixture();
            await f.Currency.EnsureSeededAsync(); // USD base + LBP @ 89,500 step 1,000
            var push = await f.Displays.PushAsync("REG-1", new DisplayPushDto(null, new() { new("Latte", 2, 3.5m), new("Cake", 1, 4.25m) }, null));
            push.Success.Should().BeTrue();
            push.Data!.Status.Should().Be(DisplayStatuses.Cart);
            push.Data.TotalUsd.Should().Be(11.25m);
            push.Data.RateLbp.Should().Be(89500m);
            push.Data.TotalLbp.Should().Be(1007000m); // 1,006,875 → 1,007,000

            var view = await f.Displays.ViewAsync(push.Data.Token);
            view.Success.Should().BeTrue();
            view.Data!.Cart.Should().HaveCount(2);
            view.Data.Cart[0].Name.Should().Be("Latte");
            view.Data.QrPayload.Should().BeNull();

            var idle = await f.Displays.PushAsync("REG-1", new DisplayPushDto(null, new(), null));
            idle.Data!.Status.Should().Be(DisplayStatuses.Idle);
            idle.Data.TotalUsd.Should().Be(0m);
            (await f.Db.Set<DisplayState>().CountAsync()).Should().Be(1, "pushing twice reuses the register's display");

            (await f.Displays.ViewAsync("bogus")).Success.Should().BeFalse();
        }

        [Fact]
        public async Task Admin_create_rejects_duplicates_and_rotate_changes_token()
        {
            using var f = new PaymentsFixture();
            var a = await f.Displays.CreateAsync(new DisplayCreateDto("Main"));
            a.Success.Should().BeTrue();
            (await f.Displays.CreateAsync(new DisplayCreateDto(" Main "))).Success.Should().BeFalse();
            (await f.Displays.CreateAsync(new DisplayCreateDto(""))).Success.Should().BeFalse();
            var rotated = await f.Displays.RotateTokenAsync(a.Data!.Id);
            rotated.Data!.Token.Should().NotBe(a.Data.Token);
            (await f.Displays.ViewAsync(a.Data.Token)).Success.Should().BeFalse("old token stops working");
            (await f.Displays.ViewAsync(rotated.Data.Token)).Success.Should().BeTrue();
            (await f.Displays.DeleteAsync(a.Data.Id)).Should().BeTrue();
            (await f.Displays.ListAsync()).Should().BeEmpty();
        }

        [Fact]
        public async Task Request_whish_shows_qr_while_awaiting_and_flips_to_paid()
        {
            using var f = new PaymentsFixture();
            f.AddTransaction(7, 12m);
            var push = await f.Displays.PushAsync("REG-2", new DisplayPushDto(7, new() { new("Tea", 1, 12m) }, null));
            var token = push.Data!.Token;

            var req = await f.Displays.RequestWhishAsync("REG-2", new DisplayRequestWhishDto(), PaymentsFixture.Success, PaymentsFixture.Cancel);
            req.Success.Should().BeTrue();
            req.Data!.Amount.Should().Be(12m);

            var awaiting = await f.Displays.ViewAsync(token);
            awaiting.Data!.Status.Should().Be(DisplayStatuses.Awaiting);
            awaiting.Data.QrPayload.Should().Be(req.Data.CollectUrl);
            awaiting.Data.WhishAmount.Should().Be(12m);

            f.Whish.Setup(w => w.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WhishCollectStatus("success", null));
            var paid = await f.Displays.ViewAsync(token);
            paid.Data!.Status.Should().Be(DisplayStatuses.Paid);
            paid.Data.QrPayload.Should().BeNull();
            f.TenderCalls.Should().HaveCount(1);
            f.Transactions.Verify(t => t.CloseOpenInvoice(7, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<decimal>()), Times.Once);

            // Till pushes the next customer's cart → back to Cart/Idle with the request cleared.
            var next = await f.Displays.PushAsync("REG-2", new DisplayPushDto(null, new(), null));
            next.Data!.Status.Should().Be(DisplayStatuses.Idle);
            next.Data.WhishRequestId.Should().BeNull();
        }

        [Fact]
        public async Task Request_whish_without_invoice_or_configuration_fails_cleanly()
        {
            using var f = new PaymentsFixture();
            var r = await f.Displays.RequestWhishAsync("REG-3", new DisplayRequestWhishDto(), PaymentsFixture.Success, PaymentsFixture.Cancel);
            r.Success.Should().BeFalse();
            r.Error.Should().Be("NoInvoice");

            f.AddTransaction(8, 5m);
            f.Whish.Setup(w => w.IsConfiguredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            var r2 = await f.Displays.RequestWhishAsync("REG-3", new DisplayRequestWhishDto(8), PaymentsFixture.Success, PaymentsFixture.Cancel);
            r2.Success.Should().BeFalse();
            r2.Error.Should().Be("NotConfigured");
            (await f.Db.Set<DisplayState>().SingleAsync()).Status.Should().Be(DisplayStatuses.Idle, "the display is untouched when the request fails");
        }
    }
}
