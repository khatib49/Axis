using FluentAssertions;
using Xunit;

namespace Tests.Ai
{
    /// <summary>
    /// Guards the rule that bit us twice already: every DateTime we hand
    /// to Npgsql for a TIMESTAMPTZ column must have Kind=Utc. Bare
    /// DateTime.Parse on a date-only string returns Kind=Unspecified.
    /// The helper below (mirroring AiToolExecutor.ParseDate) must always
    /// produce Kind=Utc no matter the input flavour.
    /// </summary>
    public class DateTimeKindTests
    {
        // This is a copy of the ParseDate behaviour from AiToolExecutor so we
        // can test it in isolation without spinning up the whole executor.
        private static DateTime ToUtcSafe(string input)
        {
            var d = DateTime.Parse(input);
            return d.Kind switch
            {
                DateTimeKind.Utc         => d,
                DateTimeKind.Local       => d.ToUniversalTime(),
                _                        => DateTime.SpecifyKind(d, DateTimeKind.Utc),
            };
        }

        [Theory]
        [InlineData("2026-06-01")]                  // date only — Kind=Unspecified
        [InlineData("2026-06-01T10:30:00")]         // local-style with no zone
        [InlineData("2026-06-01T10:30:00Z")]        // explicit UTC
        [InlineData("2026-06-01T10:30:00+02:00")]   // with offset → Kind=Local on the .NET side
        public void Result_is_always_Kind_Utc(string input)
        {
            var result = ToUtcSafe(input);
            result.Kind.Should().Be(DateTimeKind.Utc);
        }

        [Fact]
        public void Date_only_string_keeps_calendar_day()
        {
            // "2026-06-01" should map to 2026-06-01 in UTC, not yesterday/tomorrow.
            var d = ToUtcSafe("2026-06-01");
            d.Year.Should().Be(2026);
            d.Month.Should().Be(6);
            d.Day.Should().Be(1);
        }

        [Fact]
        public void Utc_input_stays_at_same_instant()
        {
            var d = ToUtcSafe("2026-06-01T10:30:00Z");
            d.Hour.Should().Be(10);
            d.Minute.Should().Be(30);
        }
    }
}
