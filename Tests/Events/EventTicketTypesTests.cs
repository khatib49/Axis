using Application.DTOs;
using Application.Services.Events;
using FluentAssertions;
using Xunit;

namespace Tests.Events
{
    /// <summary>
    /// Ticket types are edited in a form and priced on the server, so the
    /// normaliser is what stands between an admin typo and a wrong charge.
    /// </summary>
    public class EventTicketTypesTests
    {
        private static readonly IReadOnlySet<string> NoneInUse = new HashSet<string>();

        [Fact]
        public void New_types_get_keys_trimmed_names_and_cent_prices()
        {
            var r = EventTicketTypes.Normalize(new[]
            {
                new EventTicketTypeDto(null, "  Standard ", 15.004m, "  Entry + soft drinks ", 0),
                new EventTicketTypeDto(null, "VIP", 25m, null, 20),
            }, Array.Empty<EventTicketTypeDto>(), NoneInUse);

            r.Should().HaveCount(2);
            r[0].Name.Should().Be("Standard");
            r[0].Price.Should().Be(15.00m);
            r[0].Description.Should().Be("Entry + soft drinks");
            r[0].Capacity.Should().BeNull();                 // 0 = unlimited
            r[1].Capacity.Should().Be(20);
            r.Select(t => t.Key).Should().OnlyHaveUniqueItems().And.OnlyContain(k => k!.StartsWith("tt_"));
        }

        [Fact]
        public void Existing_keys_survive_a_rename_and_invented_keys_are_replaced()
        {
            var existing = new[] { new EventTicketTypeDto("tt_abc", "Standard", 15m) };
            var r = EventTicketTypes.Normalize(new[]
            {
                new EventTicketTypeDto("tt_abc", "General admission", 18m),
                new EventTicketTypeDto("tt_fromclient", "VIP", 25m),
            }, existing, NoneInUse);

            r[0].Key.Should().Be("tt_abc");
            r[1].Key.Should().NotBe("tt_fromclient");
        }

        [Fact]
        public void A_type_with_sales_is_hidden_not_deleted()
        {
            var existing = new[]
            {
                new EventTicketTypeDto("tt_std", "Standard", 15m),
                new EventTicketTypeDto("tt_vip", "VIP", 25m),
                new EventTicketTypeDto("tt_old", "Early bird", 10m),
            };
            var inUse = new HashSet<string> { "tt_vip" };

            // Admin removed VIP (has sales) and Early bird (no sales).
            var r = EventTicketTypes.Normalize(new[] { new EventTicketTypeDto("tt_std", "Standard", 15m) }, existing, inUse);

            r.Should().HaveCount(2);
            r.Single(t => t.Key == "tt_vip").IsActive.Should().BeFalse();
            r.Should().NotContain(t => t.Key == "tt_old");
        }

        [Theory]
        [InlineData("", 10, "needs a name")]
        [InlineData("VIP", -1, "negative price")]
        public void Invalid_rows_are_rejected_with_a_readable_message(string name, decimal price, string expected)
        {
            var act = () => EventTicketTypes.Normalize(new[] { new EventTicketTypeDto(null, name, price) },
                Array.Empty<EventTicketTypeDto>(), NoneInUse);
            act.Should().Throw<ArgumentException>().WithMessage($"*{expected}*");
        }

        [Fact]
        public void Duplicate_names_are_rejected()
        {
            var act = () => EventTicketTypes.Normalize(new[]
            {
                new EventTicketTypeDto(null, "VIP", 25m),
                new EventTicketTypeDto(null, "vip", 30m),
            }, Array.Empty<EventTicketTypeDto>(), NoneInUse);
            act.Should().Throw<ArgumentException>().WithMessage("*both called*");
        }

        [Fact]
        public void Json_round_trips_and_active_filters_hidden_types()
        {
            var types = new List<EventTicketTypeDto>
            {
                new("tt_a", "Standard", 15m, "Entry", null, true),
                new("tt_b", "VIP", 25m, "Front row", 10, false),
            };
            var json = EventTicketTypes.Serialize(types);

            EventTicketTypes.Parse(json).Should().BeEquivalentTo(types);
            EventTicketTypes.Active(json).Select(t => t.Key).Should().Equal("tt_a");
            EventTicketTypes.Parse(null).Should().BeEmpty();
            EventTicketTypes.Parse("not json").Should().BeEmpty();
            EventTicketTypes.Serialize(new List<EventTicketTypeDto>()).Should().BeNull();
        }
    }
}
