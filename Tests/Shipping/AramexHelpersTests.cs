using Application.Services.Shipping;
using FluentAssertions;
using Xunit;

namespace Tests.Shipping
{
    /// <summary>
    /// Pure helpers behind the Aramex integration. These run without any
    /// network or DB — run with `dotnet test` before touching the sandbox.
    /// </summary>
    public class AramexHelpersTests
    {
        [Theory]
        [InlineData("SH014", "Delivered", "InTransit", "Delivered")]
        [InlineData("SH999", "Shipment delivered to consignee", "OutForDelivery", "Delivered")]
        [InlineData("SH007", "Out for delivery", "InTransit", "OutForDelivery")]
        [InlineData("SH003", "Received at origin facility", "Created", "PickedUp")]
        [InlineData("SH005", "Departed facility", "PickedUp", "InTransit")]
        [InlineData("SH034", "Returned to shipper", "OutForDelivery", "Returned")]
        [InlineData("SH001", "Record created", "Created", "Created")]
        public void Status_map_follows_aramex_codes(string code, string description, string current, string expected)
            => AramexStatusMap.ToShipmentStatus(code, description, current).Should().Be(expected);

        [Fact]
        public void Not_delivered_text_does_not_count_as_delivered()
        {
            AramexStatusMap.ToShipmentStatus("SH013", "Not delivered - consignee not available", "OutForDelivery")
                .Should().NotBe("Delivered");
            AramexStatusMap.ToShipmentStatus("SH013", "Delivery attempt failed", "OutForDelivery")
                .Should().NotBe("Delivered");
        }

        [Fact]
        public void Unknown_code_never_regresses_a_final_status()
        {
            AramexStatusMap.ToShipmentStatus("XX", "something odd", "Delivered").Should().Be("Delivered");
            AramexStatusMap.ToShipmentStatus("XX", "something odd", "Returned").Should().Be("Returned");
        }

        [Fact]
        public void Wcf_dates_round_trip()
        {
            var utc = new DateTime(2026, 10, 2, 9, 30, 0, DateTimeKind.Utc);
            var s = WcfDate.Format(utc);
            s.Should().StartWith("/Date(").And.EndWith(")/");
            WcfDate.Parse(s).Should().Be(utc);
        }

        [Theory]
        [InlineData("/Date(1759397400000)/")]
        [InlineData("/Date(1759397400000+0300)/")]
        [InlineData("2026-10-02T09:30:00Z")]
        public void Wcf_date_parser_accepts_every_shape_aramex_sends(string raw)
            => WcfDate.Parse(raw).Should().NotBeNull();

        [Fact]
        public void Wcf_date_parser_returns_null_for_garbage()
        {
            WcfDate.Parse(null).Should().BeNull();
            WcfDate.Parse("").Should().BeNull();
            WcfDate.Parse("not a date").Should().BeNull();
        }

        [Fact]
        public void Party_json_uses_phone_as_cell_when_cell_is_missing()
        {
            var p = new AramexParty(null, "Hamra street 12", null, "Beirut", "LB", "Rami", "AXIS", "+96170000000", "", "a@b.c");
            var contact = p.ContactJson();
            var cell = contact.GetType().GetProperty("CellPhone")!.GetValue(contact);
            cell.Should().Be("+96170000000");
        }
    }
}
