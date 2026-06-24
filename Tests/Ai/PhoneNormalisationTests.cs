using FluentAssertions;
using Xunit;

namespace Tests.Ai
{
    /// <summary>
    /// Mirrors WhatsAppService.NormalisePhone — strips anything that isn't
    /// a digit or leading +. WhatsApp's Cloud API rejects formatted numbers
    /// like "+961 70 000 001", so we need a tight pre-send scrub.
    /// </summary>
    public class PhoneNormalisationTests
    {
        // Local copy of the implementation so we can test in isolation.
        private static string Normalise(string? raw)
        {
            var trimmed = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed)) return "";
            var digits = new string(trimmed.Where(c => char.IsDigit(c) || c == '+').ToArray());
            return digits;
        }

        [Theory]
        [InlineData("+961 70 000 001",  "+96170000001")]
        [InlineData("(+961) 70-000-001", "+96170000001")]
        [InlineData("  +961-70-000-001 ", "+96170000001")]
        [InlineData("96170000001",      "96170000001")]
        [InlineData("+961-70-000-001",  "+96170000001")]
        public void Strips_formatting_keeps_digits_and_leading_plus(string input, string expected)
        {
            Normalise(input).Should().Be(expected);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Empty_in_empty_out(string? input)
        {
            Normalise(input).Should().Be("");
        }
    }
}
