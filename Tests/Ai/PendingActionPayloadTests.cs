using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Tests.Ai
{
    /// <summary>
    /// The shape of the JSON payload that Claude must produce when calling
    /// the propose_* tools is part of our contract with the model. If the
    /// shape drifts, the PendingActionService.Execute* methods can't parse
    /// it and approvals will fail. These tests document the contract.
    /// </summary>
    public class PendingActionPayloadTests
    {
        // Mirror of the parser logic in PendingActionService — read fields
        // out of the JSON payload exactly as the executor would.
        private static FlashTournamentDecoded DecodeFlash(string json)
        {
            using var jd = JsonDocument.Parse(json);
            var p = jd.RootElement;
            var phones = p.GetProperty("recipient_phones").EnumerateArray()
                .Select(x => x.GetString() ?? "").ToList();
            return new FlashTournamentDecoded(
                Game: p.GetProperty("game").GetString()!,
                StartAtIso: p.GetProperty("start_at_iso").GetString()!,
                EntryFee: p.GetProperty("entry_fee").GetDecimal(),
                PrizePool: p.GetProperty("prize_pool").GetDecimal(),
                Phones: phones,
                Hype: p.GetProperty("hype_message").GetString() ?? ""
            );
        }
        private record FlashTournamentDecoded(
            string Game, string StartAtIso, decimal EntryFee,
            decimal PrizePool, List<string> Phones, string Hype);

        [Fact]
        public void FlashTournament_payload_round_trips()
        {
            var json = """
            {
              "game": "Tekken",
              "start_at_iso": "2026-06-25T16:00:00Z",
              "entry_fee": 5,
              "prize_pool": 30,
              "recipient_phones": ["+96170000001", "+96170000002"],
              "recipient_names": ["Karim", "Lara"],
              "hype_message": "Tekken tonight, you're seeded #1"
            }
            """;
            var decoded = DecodeFlash(json);
            decoded.Game.Should().Be("Tekken");
            decoded.EntryFee.Should().Be(5m);
            decoded.PrizePool.Should().Be(30m);
            decoded.Phones.Should().HaveCount(2).And.Contain("+96170000001");
            decoded.Hype.Should().Contain("Tekken");
        }

        [Fact]
        public void FlashTournament_missing_required_field_throws()
        {
            var json = """{ "game": "Tekken" }""";
            var act = () => DecodeFlash(json);
            act.Should().Throw<KeyNotFoundException>();
        }

        [Fact]
        public void CustomerPing_payload_recipients_count_matches_array_length()
        {
            var json = """
            {
              "recipients": [
                { "phone": "+96170000001", "name": "Karim",  "message": "Tonight 8pm?" },
                { "phone": "+96170000002", "name": "Lara",   "message": "Same setup as last week?" },
                { "phone": "+96170000003",                    "message": "Drop by today" }
              ],
              "reason": "3 regulars overdue for visits"
            }
            """;
            using var jd = JsonDocument.Parse(json);
            var recipients = jd.RootElement.GetProperty("recipients");
            recipients.GetArrayLength().Should().Be(3);
            // Every recipient must at least have a phone — the executor skips
            // empty-phone rows but should never crash on missing name.
            foreach (var r in recipients.EnumerateArray())
            {
                r.GetProperty("phone").GetString().Should().NotBeNullOrEmpty();
                r.GetProperty("message").GetString().Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public void Payload_with_unknown_extra_fields_is_tolerated()
        {
            // We don't want Claude adding a harmless extra field to break us.
            var json = """
            {
              "game": "FIFA",
              "start_at_iso": "2026-06-25T16:00:00Z",
              "entry_fee": 3,
              "prize_pool": 15,
              "recipient_phones": ["+961"],
              "hype_message": "Quick match?",
              "future_field_we_dont_know_about": "yo"
            }
            """;
            var decoded = DecodeFlash(json);
            decoded.Game.Should().Be("FIFA");
        }
    }
}
