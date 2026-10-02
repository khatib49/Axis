using System.Net;
using System.Text.Json;
using Application.Services.Payments;
using Domain.Entities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Tests.Payments.MontyPayTestSupport;

namespace Tests.Payments
{
    /// <summary>
    /// The session request exactly as MontyPay receives it — field names,
    /// amount format, hash, and the billing_address block that keeps the
    /// hosted page from asking for billing details / "State".
    /// </summary>
    public class MontyPaySessionTests
    {
        private sealed class CaptureHandler : HttpMessageHandler
        {
            public readonly List<(string Url, string Body)> Requests = new();
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Requests.Add((request.RequestUri!.ToString(), await request.Content!.ReadAsStringAsync(ct)));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"redirect_url\":\"https://checkout.example.test/auth/x\"}") };
            }
        }

        private static (MontyPayProvider Provider, CaptureHandler Http) Build(Dictionary<string, string?> settings)
        {
            var handler = new CaptureHandler();
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
            return (new MontyPayProvider(SettingsMock(settings).Object, factory.Object, NullLogger<MontyPayProvider>.Instance), handler);
        }

        private static OnlinePayment Payment(string? phone = "+961 70 941 151") => new()
        {
            Id = 1, Code = "abc123", Provider = "MontyPay", Environment = "sandbox", Purpose = "Custom",
            Amount = 1m, Currency = "USD", Description = "MontyPay test product 1 USD",
            CustomerName = "Axis Test", CustomerPhone = phone, Status = "Created",
        };

        [Fact]
        public async Task Session_request_matches_the_documented_shape_and_carries_billing_address()
        {
            var (provider, http) = Build(Settings());

            var res = await provider.CreateCheckoutAsync(Payment(),
                "https://www.example.test/pay/abc123/result?outcome=success",
                "https://www.example.test/pay/abc123/result?outcome=cancel",
                "https://api.example.test/api/payments/montypay/callback");

            res.Success.Should().BeTrue();
            var (url, raw) = http.Requests.Single();
            url.Should().Be("https://checkout.example.test/api/v1/session");

            var body = JsonDocument.Parse(raw).RootElement;
            body.GetProperty("merchant_key").GetString().Should().Be("sandbox-key-0000");
            body.GetProperty("operation").GetString().Should().Be("purchase");
            var order = body.GetProperty("order");
            order.GetProperty("amount").GetString().Should().Be("1.00");
            order.GetProperty("currency").GetString().Should().Be("USD");
            body.GetProperty("hash").GetString().Should().Be(MontyPayProvider.ComputeHash(
                order.GetProperty("number").GetString() + "1.00" + "USD" + "MontyPay test product 1 USD", SandboxPassword, false));

            var billing = body.GetProperty("billing_address");
            billing.GetProperty("country").GetString().Should().Be("LB");
            billing.GetProperty("state").GetString().Should().Be("Beirut");
            billing.GetProperty("city").GetString().Should().Be("Beirut");
            billing.GetProperty("address").GetString().Should().Be("Beirut");
            billing.GetProperty("zip").GetString().Should().Be("1100");
            billing.GetProperty("phone").GetString().Should().Be("96170941151");
            raw.Should().NotContain(SandboxPassword);
        }

        [Fact]
        public async Task Billing_address_can_be_switched_off()
        {
            var settings = Settings();
            settings["MontyPay.SendBillingAddress"] = "false";
            var (provider, http) = Build(settings);

            await provider.CreateCheckoutAsync(Payment(), "https://s.test/ok", "https://s.test/cancel", "");

            JsonDocument.Parse(http.Requests.Single().Body).RootElement.TryGetProperty("billing_address", out _).Should().BeFalse();
        }

        [Fact]
        public void Billing_overrides_are_used_and_bad_values_fall_back()
        {
            var b = MontyPayProvider.BuildBillingAddress(null, "lb", "Mount Lebanon", "Jounieh", "Main road 12", "1200", "+961 1 234567");
            b.Should().Contain(new KeyValuePair<string, object?>("country", "LB"))
             .And.Contain(new KeyValuePair<string, object?>("state", "Mount Lebanon"))
             .And.Contain(new KeyValuePair<string, object?>("city", "Jounieh"))
             .And.Contain(new KeyValuePair<string, object?>("phone", "9611234567"));   // no customer phone → fallback

            var bad = MontyPayProvider.BuildBillingAddress("12", "Lebanon", "X", new string('c', 50), "", "123456789012", null);
            bad["country"].Should().Be("LB");        // not a 2-letter code
            bad["state"].Should().Be("Beirut");      // < 2 chars
            bad["city"].Should().Be("Beirut");       // > 40 chars
            bad["address"].Should().Be("Beirut");
            bad["zip"].Should().Be("1100");          // > 10 chars
            bad.ContainsKey("phone").Should().BeFalse();   // too short to be a phone
        }
    }
}
