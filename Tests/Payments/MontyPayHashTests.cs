using System.Globalization;
using Application.Services.Payments;
using FluentAssertions;
using Xunit;

namespace Tests.Payments
{
    /// <summary>
    /// The hash is the only thing between a forged POST and a "Paid" ticket.
    /// These vectors are copied from docs.montypay.com → Hash signature
    /// (password "m3rch4ntP4ss"), so a regression fails here, not in sandbox.
    /// </summary>
    public class MontyPayHashTests
    {
        private const string DocPassword = "m3rch4ntP4ss";
        private const string DocPaymentId = "50a1361a-7c2e-11f1-b8d4-0242ac120002";

        [Fact]
        public void Session_hash_matches_the_documented_example()
        {
            // order.number + order.amount + order.currency + order.description + password
            var concat = "order-1234" + "10.00" + "USD" + "Important gift";
            MontyPayProvider.ComputeHash(concat, DocPassword, useSha256: false)
                .Should().Be("29478736ac36042734a48df28522e1ddb9255cf3");
        }

        [Fact]
        public void Callback_hash_matches_the_documented_example()
        {
            // id + order_number + order_amount + order_currency + order_description + password
            var concat = DocPaymentId + "order-1234" + "10.00" + "USD" + "Important gift";
            MontyPayProvider.ComputeHash(concat, DocPassword, useSha256: false)
                .Should().Be("9eddb423ef8a8c2e36e8717eb5e638cdeb17d957");
        }

        [Fact]
        public void Status_by_payment_id_hash_matches_the_documented_example()
            => MontyPayProvider.ComputeHash(DocPaymentId, DocPassword, useSha256: false)
                .Should().Be("e505b0a63333c82df960b878530a4980c82cfc69");

        [Fact]
        public void Status_by_order_id_hash_matches_the_documented_example()
            => MontyPayProvider.ComputeHash("order-1234", DocPassword, useSha256: false)
                .Should().Be("8d2d3032e471f6782aa070301d6de8aeb807f6fc");

        [Fact]
        public void Sha256_mode_is_sha1_of_sha256_hex()
        {
            // The docs give the formula — sha1(sha256(UPPER(concat))) — but no
            // worked value; this one was computed independently with Python's
            // hashlib from the same documented inputs.
            var concat = "order-1234" + "10.00" + "USD" + "Important gift";
            var sha = MontyPayProvider.ComputeHash(concat, DocPassword, useSha256: true);
            sha.Should().Be("032fff8973d1350b3ce5d59f55a9b616bce7c0bd");
            sha.Should().NotBe(MontyPayProvider.ComputeHash(concat, DocPassword, useSha256: false));
        }

        [Fact]
        public void Hash_is_case_insensitive_on_input_but_lowercase_hex_out()
        {
            var a = MontyPayProvider.ComputeHash("ORDER-1234", "M3RCH4NTP4SS", false);
            var b = MontyPayProvider.ComputeHash("order-1234", "m3rch4ntP4ss", false);
            a.Should().Be(b);
            a.Should().MatchRegex("^[0-9a-f]{40}$");
        }

        [Theory]
        [InlineData("1", "1.00")]
        [InlineData("1.5", "1.50")]
        [InlineData("10", "10.00")]
        [InlineData("1234.567", "1234.57")]
        [InlineData("0.19", "0.19")]
        public void Amount_is_always_two_decimals_with_a_dot(string input, string expected)
        {
            var prev = CultureInfo.CurrentCulture;
            try
            {
                // A comma-decimal server culture must not leak into the hash.
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                MontyPayProvider.FormatAmount(decimal.Parse(input, CultureInfo.InvariantCulture)).Should().Be(expected);
            }
            finally { CultureInfo.CurrentCulture = prev; }
        }

        [Theory]
        [InlineData("AXIS order WEB-1234", "AXIS order WEB-1234")]
        [InlineData("Café — night", "Caf- - night")]   // non-ASCII → '-' so request and callback uppercase the same
        [InlineData("line1\nline2", "line1 line2")]
        [InlineData("", "Payment")]
        [InlineData("x", "Payment")]                    // API minLength 2
        [InlineData(null, "Payment")]
        public void Description_is_ascii_single_line_and_valid_length(string? input, string expected)
            => MontyPayProvider.SafeDescription(input).Should().Be(expected);

        [Fact]
        public void Description_is_capped()
            => MontyPayProvider.SafeDescription(new string('a', 500)).Length.Should().BeLessOrEqualTo(120);

        [Theory]
        [InlineData("Rami Khatib", "rami@example.com", true, true)]
        [InlineData("R", "rami@example.com", false, true)]        // name < 2 chars → dropped
        [InlineData("Rami", "not-an-email", true, false)]          // bad email → dropped, not a failed session
        [InlineData(null, null, false, false)]
        public void Customer_block_only_carries_values_the_api_accepts(string? name, string? email, bool hasName, bool hasEmail)
        {
            var c = MontyPayProvider.BuildCustomer(name, email);
            if (!hasName && !hasEmail) { c.Should().BeNull(); return; }
            c!.ContainsKey("name").Should().Be(hasName);
            c.ContainsKey("email").Should().Be(hasEmail);
        }

        // ── Outcome table (Callbacks guide) ──────────────────────────────
        [Theory]
        [InlineData("sale", "success", "settled", "Paid")]
        [InlineData("capture", "success", "settled", "Paid")]
        [InlineData("SALE", "SUCCESS", "SETTLED", "Paid")]
        [InlineData("sale", "fail", "decline", "Failed")]
        [InlineData("sale", null, "declined", "Failed")]           // status-check spelling
        [InlineData("sale", "success", "pending", null)]           // DMS auth — not money yet
        [InlineData("sale", "waiting", "prepare", null)]
        [InlineData("sale", "undefined", "prepare", null)]
        [InlineData("3ds", "success", "3ds", null)]
        [InlineData("3ds", "success", "settled", null)]            // only a settling TYPE can make it Paid
        [InlineData("redirect", "success", "redirect", null)]
        [InlineData("init", "success", "prepare", null)]
        [InlineData("refund", "success", "refund", "Refunded")]
        [InlineData("refund", "fail", "settled", null)]            // failed refund leaves it Paid
        [InlineData("void", "success", "void", "Voided")]
        [InlineData("reversal", "success", "reversal", "Voided")]
        [InlineData("chargeback", "success", "chargeback", "Chargeback")]
        public void Outcome_follows_the_documented_decision_table(string? type, string? status, string? orderStatus, string? expected)
            => MontyPayProvider.MapOutcome(type, status, orderStatus).Should().Be(expected);
    }
}
