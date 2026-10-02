using System.Reflection;
using System.Text;
using Application.IServices;
using AxisAPI.Controllers;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Tests.Payments.MontyPayTestSupport;

namespace Tests.Payments
{
    /// <summary>
    /// Callback parsing, in two layers: the HTTP endpoint turns MontyPay's
    /// form-urlencoded POST into key/values; the provider verifies the hash
    /// and normalises it.
    /// </summary>
    public class MontyPayCallbackParsingTests
    {
        // ── Provider: hash verification + normalisation ─────────────────
        [Fact]
        public async Task Documented_callback_vector_verifies()
        {
            var settings = Settings();
            settings["MontyPay.Sandbox.Password"] = "m3rch4ntP4ss";
            var form = new Dictionary<string, string>
            {
                ["id"] = "50a1361a-7c2e-11f1-b8d4-0242ac120002",
                ["order_number"] = "order-1234",
                ["order_amount"] = "10.00",
                ["order_currency"] = "USD",
                ["order_description"] = "Important gift",
                ["order_status"] = "settled",
                ["type"] = "sale",
                ["status"] = "success",
                ["hash"] = "9eddb423ef8a8c2e36e8717eb5e638cdeb17d957",
            };

            var cb = await Provider(settings).ParseCallbackAsync(form, ToRaw(form));

            cb.HashValid.Should().BeTrue();
            cb.Outcome.Should().Be("Paid");
            cb.Amount.Should().Be(10.00m);
            cb.OrderNumber.Should().Be("order-1234");
            cb.ProviderPaymentId.Should().Be("50a1361a-7c2e-11f1-b8d4-0242ac120002");
        }

        [Fact]
        public async Task Signed_settled_sale_parses_to_Paid()
        {
            var form = SignedCallback("AX-abc-1", "1.00", "USD", "Test product 1 USD");
            var cb = await Provider(Settings()).ParseCallbackAsync(form, ToRaw(form));

            cb.HashValid.Should().BeTrue();
            cb.Outcome.Should().Be("Paid");
            cb.Amount.Should().Be(1.00m);
            cb.Currency.Should().Be("USD");
            cb.CardMasked.Should().Be("411111****1111");
            cb.ProviderType.Should().Be("sale");
            cb.OrderStatus.Should().Be("settled");
        }

        [Theory]
        [InlineData("order_amount", "100.00")]
        [InlineData("order_currency", "EUR")]
        [InlineData("order_description", "something else")]
        [InlineData("order_number", "AX-other-1")]
        [InlineData("id", "00000000-0000-0000-0000-000000000000")]
        public async Task Any_tampered_signed_field_breaks_the_hash(string field, string value)
        {
            var form = SignedCallback("AX-abc-1", "1.00", "USD", "Test product 1 USD");
            form[field] = value;
            (await Provider(Settings()).ParseCallbackAsync(form, ToRaw(form))).HashValid.Should().BeFalse();
        }

        [Fact]
        public async Task Wrong_password_or_missing_hash_is_rejected()
        {
            var forged = SignedCallback("AX-abc-1", "1.00", "USD", "x y", password: "guess");
            (await Provider(Settings()).ParseCallbackAsync(forged, ToRaw(forged))).HashValid.Should().BeFalse();

            var unsigned = SignedCallback("AX-abc-1", "1.00", "USD", "x y");
            unsigned.Remove("hash");
            (await Provider(Settings()).ParseCallbackAsync(unsigned, ToRaw(unsigned))).HashValid.Should().BeFalse();
        }

        [Fact]
        public async Task Sha256_signed_callback_verifies_whichever_mode_is_configured()
        {
            var form = SignedCallback("AX-abc-1", "1.00", "USD", "Test", sha256: true);
            (await Provider(Settings("sha256")).ParseCallbackAsync(form, ToRaw(form))).HashValid.Should().BeTrue();
            // A wrong HashAlgorithm setting must not make real callbacks bounce.
            (await Provider(Settings("md5")).ParseCallbackAsync(form, ToRaw(form))).HashValid.Should().BeTrue();
        }

        [Fact]
        public async Task Production_signed_callback_verifies_while_sandbox_is_active()
        {
            var form = SignedCallback("AX-abc-1", "1.00", "USD", "Test", password: ProductionPassword);
            (await Provider(Settings(withProduction: true)).ParseCallbackAsync(form, ToRaw(form))).HashValid.Should().BeTrue();
        }

        [Fact]
        public async Task Declined_sale_parses_to_Failed_with_reason()
        {
            var form = SignedCallback("AX-abc-1", "1.00", "USD", "Test", status: "fail", orderStatus: "decline");
            form["reason"] = "Declined by processing.";
            var cb = await Provider(Settings()).ParseCallbackAsync(form, ToRaw(form));
            cb.HashValid.Should().BeTrue();
            cb.Outcome.Should().Be("Failed");
            cb.Reason.Should().Be("Declined by processing.");
        }

        // ── HTTP endpoint ───────────────────────────────────────────────
        [Fact]
        public void Callback_endpoint_is_anonymous_POST_on_the_documented_route()
        {
            var m = typeof(PaymentsController).GetMethod(nameof(PaymentsController.MontyPayCallback))!;
            m.GetCustomAttribute<AllowAnonymousAttribute>().Should().NotBeNull();
            m.GetCustomAttribute<HttpPostAttribute>()!.Template.Should().Be("montypay/callback");
            typeof(PaymentsController).GetCustomAttribute<RouteAttribute>()!.Template.Should().Be("api/payments");
        }

        [Theory]
        [InlineData("application/x-www-form-urlencoded; charset=utf-8")]
        [InlineData("application/x-www-form-urlencoded")]
        [InlineData(null)]   // no content-type header at all
        public async Task Form_body_reaches_the_service_decoded_and_answers_200(string? contentType)
        {
            // The documented callback example, '+' for spaces and all.
            const string body =
                "id=f0a51dfa-fc43-11ec-8128-0242ac120004&order_number=order-1234&order_amount=3.01&order_currency=USD" +
                "&order_description=Important+gift&order_status=settled&type=sale&status=success&card=411111****1111" +
                "&date=2022-07-05+09:22:09&hash=6d8d440e25bdfc5288616ce567496948d2562852";

            IReadOnlyDictionary<string, string>? seen = null;
            string? seenRaw = null;
            var svc = new Mock<IOnlinePaymentService>();
            svc.Setup(s => s.HandleCallbackAsync("MontyPay", It.IsAny<IReadOnlyDictionary<string, string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
               .Callback((string _, IReadOnlyDictionary<string, string> f, string r, CancellationToken _) => { seen = f; seenRaw = r; })
               .Returns(Task.CompletedTask);

            var ctx = new DefaultHttpContext();
            ctx.Request.Method = "POST";
            if (contentType != null) ctx.Request.ContentType = contentType;
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            var controller = new PaymentsController(svc.Object, NullLogger<PaymentsController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = ctx } };

            var result = await controller.MontyPayCallback(CancellationToken.None);

            result.Should().BeOfType<OkResult>();
            seenRaw.Should().Be(body);
            seen.Should().NotBeNull();
            seen!["order_description"].Should().Be("Important gift");
            seen["date"].Should().Be("2022-07-05 09:22:09");
            seen["card"].Should().Be("411111****1111");
            seen["hash"].Should().Be("6d8d440e25bdfc5288616ce567496948d2562852");
            seen["ORDER_NUMBER"].Should().Be("order-1234");   // case-insensitive keys
        }

        [Fact]
        public async Task Callback_still_answers_200_when_processing_throws_upstream()
        {
            // HandleCallbackAsync never throws by contract, but if the body is
            // unreadable the controller itself must still answer 2xx.
            var ctx = new DefaultHttpContext();
            ctx.Request.Method = "POST";
            ctx.Request.ContentType = "application/json";
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{not json"));
            var controller = new PaymentsController(new Mock<IOnlinePaymentService>().Object, NullLogger<PaymentsController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = ctx } };

            (await controller.MontyPayCallback(CancellationToken.None)).Should().BeOfType<OkResult>();
        }
    }
}
