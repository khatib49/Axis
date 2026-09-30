using Application.Services.Events;
using FluentAssertions;
using Xunit;

namespace Tests.Events
{
    /// <summary>
    /// The ticket code is the only key to the public ticket page and what
    /// the door scans — normalisation must accept what scanners and humans
    /// produce, and generation must be unguessable and readable.
    /// </summary>
    public class TicketCodesTests
    {
        [Fact]
        public void New_codes_have_the_right_shape_and_are_unique()
        {
            var codes = Enumerable.Range(0, 500).Select(_ => TicketCodes.NewRandom()).ToList();
            codes.Should().OnlyContain(c => c.Length == 13 && c.StartsWith("TK-"));
            codes.Should().OnlyContain(c => c.Substring(3).All(ch => TicketCodes.Alphabet.Contains(ch)));
            codes.Distinct().Count().Should().Be(codes.Count);
            codes.Should().OnlyContain(c => !c.Contains('0') && !c.Contains('O') && !c.Contains('1') && !c.Contains('I'));
        }

        [Theory]
        [InlineData("TK-ABCDEFGHJK", "TK-ABCDEFGHJK")]
        [InlineData("tk-abcdefghjk", "TK-ABCDEFGHJK")]
        [InlineData("  TK-ABCDEFGHJK  ", "TK-ABCDEFGHJK")]
        [InlineData("ABCDEFGHJK", "TK-ABCDEFGHJK")]                                   // body only
        [InlineData("https://www.axislb.com/tickets/TK-ABCDEFGHJK", "TK-ABCDEFGHJK")] // scanned QR
        [InlineData("https://www.axislb.com/tickets/TK-ABCDEFGHJK?paid=1", "TK-ABCDEFGHJK")]
        [InlineData("TK-ABCDEFGHJK#x", "TK-ABCDEFGHJK")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void Normalise_accepts_scanner_and_human_input(string? raw, string expected)
            => TicketCodes.Normalise(raw).Should().Be(expected);

        [Theory]
        [InlineData("TK-ABCDEFGHJK", true)]
        [InlineData("TK-1A2B3C4D5E", true)]   // hex codes from the SQL backfill
        [InlineData("TK-ABC", false)]
        [InlineData("XX-ABCDEFGHJK", false)]
        [InlineData("", false)]
        public void LooksValid_checks_prefix_length_and_alphabet(string code, bool ok)
            => TicketCodes.LooksValid(code).Should().Be(ok);
    }
}
