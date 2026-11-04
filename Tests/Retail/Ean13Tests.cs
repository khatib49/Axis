using Application.Services.Retail;
using FluentAssertions;
using Xunit;

namespace Tests.Retail
{
    public class Ean13Tests
    {
        [Theory]
        [InlineData("400638133393", 1)]   // well-known sample 4006381333931
        [InlineData("590123412345", 7)]   // 5901234123457
        [InlineData("200000009000", 9)]   // in-house code for item 9 (2×1 + 9×1 = 11 → 9)
        public void Check_digit_matches_the_gs1_algorithm(string payload, int expected)
            => Ean13.CheckDigit(payload).Should().Be(expected);

        [Fact]
        public void Build_uses_venue_prefix_200_zero_padded_ids_and_a_valid_check_digit()
        {
            var code = Ean13.Build(9, null);
            code.Should().Be("2000000090009");
            Ean13.IsValid(code!).Should().BeTrue();

            var variant = Ean13.Build(10, 2);
            variant.Should().HaveLength(13).And.StartWith("200000010002");
            Ean13.IsValid(variant!).Should().BeTrue();
            variant.Should().NotBe(Ean13.Build(10, null), "the variant gets its own code");
        }

        [Fact]
        public void Build_refuses_ids_that_do_not_fit()
        {
            Ean13.Build(1_000_000, null).Should().BeNull();
            Ean13.Build(1, 1000).Should().BeNull();
            Ean13.Build(0, null).Should().BeNull();
        }

        [Fact]
        public void IsValid_rejects_a_wrong_check_digit_and_non_digits()
        {
            Ean13.IsValid("2000000090008").Should().BeFalse();
            Ean13.IsValid("200000009000A").Should().BeFalse();
            Ean13.IsValid("20000000900").Should().BeFalse();
        }

        [Fact]
        public void Normalise_strips_every_whitespace_including_scanner_cr_lf()
            => Ean13.Normalise(" 2000 0000 90009\r\n").Should().Be("2000000090009");
    }
}
