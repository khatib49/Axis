using Domain.Entities;
using FluentAssertions;
using Xunit;

namespace Tests.Payments2
{
    public class PublicTokenTests
    {
        [Fact]
        public async Task Self_order_session_token_must_own_the_invoice()
        {
            using var f = new PaymentsFixture();
            f.AddTransaction(10, 15m);
            f.AddTransaction(11, 15m);
            var token = Guid.NewGuid();
            f.Db.Add(new SelfOrderSession { TableCode = "TBL-1", Token = token, TransactionId = 10, Status = SelfOrderSessionStatuses.Submitted });
            await f.Db.SaveChangesAsync();

            (await f.Payments.ValidatePublicTokenAsync(token.ToString(), 10)).Should().BeTrue();
            (await f.Payments.ValidatePublicTokenAsync(token.ToString(), 11)).Should().BeFalse();
            (await f.Payments.ValidatePublicTokenAsync(Guid.NewGuid().ToString(), 10)).Should().BeFalse();
            (await f.Payments.ValidatePublicTokenAsync("", 10)).Should().BeFalse();
            (await f.Payments.ValidatePublicTokenAsync(null, 10)).Should().BeFalse();
        }

        [Fact]
        public async Task Kiosk_token_must_be_active_and_have_placed_the_order()
        {
            using var f = new PaymentsFixture();
            f.AddTransaction(20, 15m);
            f.AddTransaction(21, 15m);
            f.Db.Add(new KioskDevice { Id = 1, Name = "Front", Token = "kiosk-token-front", IsActive = true });
            f.Db.Add(new KioskDevice { Id = 2, Name = "Back", Token = "kiosk-token-back", IsActive = false });
            f.Db.Add(new DeliveryOrder { TransactionId = 20, Type = DeliveryOrderTypes.Takeaway, CustomerName = "Kiosk guest", CreatedBy = "kiosk:Front", ExternalSource = "kiosk" });
            f.Db.Add(new DeliveryOrder { TransactionId = 21, Type = DeliveryOrderTypes.Takeaway, CustomerName = "Kiosk guest", CreatedBy = "kiosk:Back", ExternalSource = "kiosk" });
            await f.Db.SaveChangesAsync();

            (await f.Payments.ValidatePublicTokenAsync("kiosk-token-front", 20)).Should().BeTrue();
            (await f.Payments.ValidatePublicTokenAsync("kiosk-token-front", 21)).Should().BeFalse("another kiosk placed that order");
            (await f.Payments.ValidatePublicTokenAsync("kiosk-token-back", 21)).Should().BeFalse("the device is inactive");
            (await f.Payments.ValidatePublicTokenAsync("nope", 20)).Should().BeFalse();
        }
    }
}
