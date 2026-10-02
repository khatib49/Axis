using Application.DTOs;
using Application.IServices;
using Application.Services;
using Application.Services.Payments;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.IRepositories;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Tests.Payments.MontyPayTestSupport;

namespace Tests.Payments
{
    /// <summary>
    /// The Online Payments ledger end to end, on an in-memory database:
    /// pay link → signed MontyPay callback → Paid → fulfilment by purpose,
    /// plus every way a callback can be wrong, repeated or blow up.
    /// The gateway HTTP call is the only thing not exercised (it needs MontyPay).
    /// </summary>
    public class OnlinePaymentFlowTests : IDisposable
    {
        private readonly string _dbName = Guid.NewGuid().ToString();
        private readonly ApplicationDbContext _db;
        private readonly OnlinePaymentService _svc;
        private readonly Mock<IEventRegistrationService> _events = new();
        private readonly Mock<IShopService> _shop = new();

        /// <summary>Fake MontyPay status API: what /api/v1/payment/status answers, and every request it saw.</summary>
        private Func<HttpRequestMessage, HttpResponseMessage> _gateway =
            _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { Content = new StringContent("{\"error_message\":\"Payment not found\"}") };
        private readonly List<string> _gatewayCalls = new();

        private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request));
        }

        public OnlinePaymentFlowTests()
        {
            _db = NewContext();
            var settings = SettingsMock(Settings());
            var http = new Mock<IHttpClientFactory>();
            http.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new FakeHandler(req =>
            {
                _gatewayCalls.Add(req.RequestUri!.AbsolutePath + " " + req.Content!.ReadAsStringAsync().Result);
                return _gateway(req);
            })));
            var provider = new MontyPayProvider(settings.Object, http.Object, NullLogger<MontyPayProvider>.Instance);

            var sp = new ServiceCollection()
                .AddSingleton(_events.Object)
                .AddSingleton(_shop.Object)
                .AddSingleton<IBaseRepository<OnlineOrder>>(new BaseRepository<OnlineOrder>(_db))
                .BuildServiceProvider();

            _svc = new OnlinePaymentService(
                new BaseRepository<OnlinePayment>(_db),
                new BaseRepository<OnlinePaymentEvent>(_db),
                new IOnlinePaymentProvider[] { provider },
                settings.Object,
                new UnitOfWork(_db),
                new Mock<IHttpContextAccessor>().Object,
                sp,
                NullLogger<OnlinePaymentService>.Instance);
        }

        private ApplicationDbContext NewContext() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        public void Dispose() => _db.Dispose();

        /// <summary>What the DB really holds — a fresh context, nothing cached.</summary>
        private (OnlinePayment Payment, List<OnlinePaymentEvent> Events) Persisted(int id)
        {
            using var db = NewContext();
            return (db.OnlinePayments.AsNoTracking().Single(p => p.Id == id),
                    db.OnlinePaymentEvents.AsNoTracking().Where(e => e.OnlinePaymentId == id).OrderBy(e => e.Id).ToList());
        }

        /// <summary>A link the customer has opened: what StartCheckoutAsync leaves behind after MontyPay answered.</summary>
        private async Task<(int Id, string OrderNumber)> RedirectedLinkAsync(
            decimal amount = 1m, string purpose = "Custom", int? referenceId = null, string description = "Test product 1 USD")
        {
            var dto = await _svc.CreateAsync(new OnlinePaymentCreateDto(amount, description, "USD",
                CustomerName: "Test Buyer", Purpose: purpose, ReferenceId: referenceId), "admin");
            var row = await _db.OnlinePayments.SingleAsync(p => p.Id == dto.Id);
            row.ProviderOrderNumber = $"AX-{row.Code}-12345678";
            row.Status = "Redirected";
            await _db.SaveChangesAsync();
            return (row.Id, row.ProviderOrderNumber);
        }

        private Task CallbackAsync(Dictionary<string, string> form) =>
            _svc.HandleCallbackAsync("MontyPay", form, ToRaw(form));

        private static Dictionary<string, string> Settled(string orderNumber, string amount = "1.00", string? id = null) =>
            SignedCallback(orderNumber, amount, "USD", "Test product 1 USD", id: id);

        // ── The 1 USD pay link MontyPay asked for ───────────────────────
        [Fact]
        public async Task Settled_callback_marks_a_pay_link_Paid_and_logs_it()
        {
            var (id, on) = await RedirectedLinkAsync();

            await CallbackAsync(Settled(on));

            var (p, ev) = Persisted(id);
            p.Status.Should().Be("Paid");
            p.PaidOn.Should().NotBeNull();
            p.IsFulfilled.Should().BeTrue();          // Custom link: nothing to apply
            p.CallbackCount.Should().Be(1);
            p.CardMasked.Should().Be("411111****1111");
            p.ProviderStatus.Should().Be("settled");
            ev.Select(e => e.Kind).Should().Equal("created", "callback", "fulfilled");
            ev[1].HashValid.Should().BeTrue();
            ev[1].ResultStatus.Should().Be("Paid");
            ev[1].Raw.Should().Contain("order_status=settled");
        }

        [Fact]
        public async Task Repeated_callback_is_logged_but_applied_once()
        {
            var (id, on) = await RedirectedLinkAsync(10m, "EventTicket", referenceId: 77, description: "Gala - Entry Ticket #77");
            _events.Setup(e => e.MarkPaidByProviderRefAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                   .ReturnsAsync(true);
            var cb = SignedCallback(on, "10.00", "USD", "Gala - Entry Ticket #77");

            await CallbackAsync(cb);
            await CallbackAsync(cb);

            var (p, ev) = Persisted(id);
            p.Status.Should().Be("Paid");
            p.CallbackCount.Should().Be(2);
            ev.Count(e => e.Kind == "callback").Should().Be(2);
            ev.Should().Contain(e => e.Note == "Duplicate — ignored");
            _events.Verify(e => e.MarkPaidByProviderRefAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task Forged_callback_is_logged_and_changes_nothing()
        {
            var (id, on) = await RedirectedLinkAsync();
            var forged = SignedCallback(on, "1.00", "USD", "Test product 1 USD", password: "attacker-guess");

            await CallbackAsync(forged);

            var (p, ev) = Persisted(id);
            p.Status.Should().Be("Redirected");
            p.PaidOn.Should().BeNull();
            p.IsFulfilled.Should().BeFalse();
            p.CallbackCount.Should().Be(1);
            var rejected = ev.Single(e => e.Kind == "callback");
            rejected.HashValid.Should().BeFalse();
            rejected.Note.Should().Be("Rejected: hash mismatch");
            rejected.Raw.Should().Contain("hash=" + forged["hash"]);   // kept verbatim for forensics
        }

        [Fact]
        public async Task Forged_callback_cannot_poison_the_duplicate_check()
        {
            var (id, on) = await RedirectedLinkAsync();
            var real = Settled(on);
            var forged = new Dictionary<string, string>(real) { ["hash"] = new string('0', 40) };

            await CallbackAsync(forged);
            await CallbackAsync(real);

            Persisted(id).Payment.Status.Should().Be("Paid");
        }

        [Fact]
        public async Task Settled_for_the_wrong_amount_is_held_not_fulfilled()
        {
            var (id, on) = await RedirectedLinkAsync(25m);

            await CallbackAsync(SignedCallback(on, "1.00", "USD", "Test product 1 USD"));

            var (p, _) = Persisted(id);
            p.Status.Should().Be("Pending");
            p.IsFulfilled.Should().BeFalse();
            p.FailureReason.Should().Contain("NOT fulfilled");
        }

        [Fact]
        public async Task Settled_in_the_wrong_currency_is_held_not_fulfilled()
        {
            var (id, on) = await RedirectedLinkAsync();

            await CallbackAsync(SignedCallback(on, "1.00", "EUR", "Test product 1 USD"));

            Persisted(id).Payment.Status.Should().Be("Pending");
        }

        [Fact]
        public async Task Intermediate_3ds_then_sale_only_the_sale_settles()
        {
            var (id, on) = await RedirectedLinkAsync();
            var pid = Guid.NewGuid().ToString();

            await CallbackAsync(SignedCallback(on, "1.00", "USD", "Test product 1 USD", type: "3ds", status: "success", orderStatus: "3ds", id: pid));
            Persisted(id).Payment.Status.Should().Be("Redirected");

            await CallbackAsync(Settled(on, id: pid));
            Persisted(id).Payment.Status.Should().Be("Paid");
        }

        [Fact]
        public async Task Decline_keeps_the_link_payable_and_a_retry_can_settle()
        {
            var (id, on) = await RedirectedLinkAsync();

            var decline = SignedCallback(on, "1.00", "USD", "Test product 1 USD", status: "fail", orderStatus: "decline");
            decline["reason"] = "Declined by processing.";
            await CallbackAsync(decline);

            var (failed, _) = Persisted(id);
            failed.Status.Should().Be("Failed");
            failed.FailureReason.Should().Be("Declined by processing.");
            (await _svc.GetPublicAsync(failed.Code))!.CanPay.Should().BeTrue();

            await CallbackAsync(Settled(on));   // second attempt, new gateway id
            Persisted(id).Payment.Status.Should().Be("Paid");
        }

        [Fact]
        public async Task Late_decline_from_another_attempt_never_unpays()
        {
            var (id, on) = await RedirectedLinkAsync();
            await CallbackAsync(Settled(on));
            await CallbackAsync(SignedCallback(on, "1.00", "USD", "Test product 1 USD", status: "fail", orderStatus: "decline"));

            Persisted(id).Payment.Status.Should().Be("Paid");
        }

        [Fact]
        public async Task Callback_for_a_superseded_session_is_matched_by_code()
        {
            var (id, on) = await RedirectedLinkAsync();
            var olderSession = on[..on.LastIndexOf('-')] + "-99999999";

            await CallbackAsync(Settled(olderSession));

            Persisted(id).Payment.Status.Should().Be("Paid");
        }

        [Fact]
        public async Task Unknown_order_is_ignored_without_throwing()
        {
            var act = () => CallbackAsync(Settled("AX-doesnotexist-1"));
            await act.Should().NotThrowAsync();
            (await _db.OnlinePayments.CountAsync()).Should().Be(0);
        }

        // ── Event ticket ────────────────────────────────────────────────
        [Fact]
        public async Task Ticket_payment_confirms_the_registration_by_its_OP_reference()
        {
            var (id, on) = await RedirectedLinkAsync(10m, "EventTicket", referenceId: 42, description: "Gala - Entry Ticket #42");
            _events.Setup(e => e.MarkPaidByProviderRefAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                   .ReturnsAsync(true);
            _events.Setup(e => e.TicketCodeForRegistrationAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync("TK-ABCDEFGHJK");

            await CallbackAsync(SignedCallback(on, "10.00", "USD", "Gala - Entry Ticket #42"));

            var (p, _) = Persisted(id);
            p.Status.Should().Be("Paid");
            p.IsFulfilled.Should().BeTrue();
            _events.Verify(e => e.MarkPaidByProviderRefAsync($"OP:{p.Code}", It.IsAny<string?>(), It.IsAny<CancellationToken>(), 42), Times.Once);

            var pub = await _svc.GetPublicAsync(p.Code);
            pub!.NextUrl.Should().Be("/tickets/TK-ABCDEFGHJK");
            pub.NextLabel.Should().Be("View your ticket");
        }

        // ── Shop order: fulfilment failures must not lose the money ────
        [Fact]
        public async Task Fulfilment_failure_keeps_Paid_records_the_error_and_Reconcile_retries()
        {
            var (id, on) = await RedirectedLinkAsync(30m, "OnlineOrder", referenceId: 9, description: "AXIS order WEB-9");
            _shop.SetupSequence(s => s.MarkPaidAsync(9, id, It.IsAny<CancellationToken>()))
                 .ThrowsAsync(new InvalidOperationException("Could not create the paid invoice for the order."))
                 .Returns(Task.CompletedTask);

            await CallbackAsync(SignedCallback(on, "30.00", "USD", "AXIS order WEB-9"));

            var (p, ev) = Persisted(id);
            p.Status.Should().Be("Paid");                       // the money is recorded no matter what
            p.IsFulfilled.Should().BeFalse();
            p.FulfillmentError.Should().Contain("paid invoice");
            ev.Should().Contain(e => e.Kind == "fulfilled" && e.Note!.StartsWith("FAILED"));

            var dto = await _svc.ReconcileAsync(id, "admin");   // admin presses Reconcile
            dto!.IsFulfilled.Should().BeTrue();
            Persisted(id).Payment.IsFulfilled.Should().BeTrue();
            _shop.Verify(s => s.MarkPaidAsync(9, id, It.IsAny<CancellationToken>()), Times.Exactly(2));
        }

        [Fact]
        public async Task Fulfilment_that_resets_the_shared_context_still_persists_Paid_and_the_outcome()
        {
            // The journal service clears the change tracker on a failed post;
            // that used to detach the payment row and drop its state.
            var (id, on) = await RedirectedLinkAsync(30m, "OnlineOrder", referenceId: 9, description: "AXIS order WEB-9");
            _shop.Setup(s => s.MarkPaidAsync(9, id, It.IsAny<CancellationToken>()))
                 .Returns(() => { _db.ChangeTracker.Clear(); return Task.CompletedTask; });

            await CallbackAsync(SignedCallback(on, "30.00", "USD", "AXIS order WEB-9"));

            var (p, ev) = Persisted(id);
            p.Status.Should().Be("Paid");
            p.IsFulfilled.Should().BeTrue();
            ev.Select(e => e.Kind).Should().Contain(new[] { "callback", "fulfilled" });
        }

        [Fact]
        public async Task Fulfilment_that_resets_the_context_and_throws_still_records_the_error()
        {
            var (id, on) = await RedirectedLinkAsync(30m, "OnlineOrder", referenceId: 9, description: "AXIS order WEB-9");
            _shop.Setup(s => s.MarkPaidAsync(9, id, It.IsAny<CancellationToken>()))
                 .Returns(() => { _db.ChangeTracker.Clear(); throw new InvalidOperationException("db error"); });

            await CallbackAsync(SignedCallback(on, "30.00", "USD", "AXIS order WEB-9"));

            var (p, _) = Persisted(id);
            p.Status.Should().Be("Paid");
            p.IsFulfilled.Should().BeFalse();
            p.FulfillmentError.Should().Be("db error");
        }

        // ── Starting checkout ───────────────────────────────────────────
        [Fact]
        public async Task Every_Pay_click_gets_a_fresh_single_use_session()
        {
            // MontyPay's checkout URL dies once opened ("Your session has expired"),
            // so Back/Close → Pay again must never be handed the old one.
            var n = 0;
            _gateway = req => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"redirect_url\":\"https://checkout.example.test/auth/token{++n}\"}}"),
            };
            var dto = await _svc.CreateAsync(new OnlinePaymentCreateDto(1m, "Test product 1 USD"), "admin");

            var first = await _svc.StartCheckoutAsync(dto.Code);
            var firstOrder = Persisted(dto.Id).Payment.ProviderOrderNumber;
            await Task.Delay(5);
            var second = await _svc.StartCheckoutAsync(dto.Code);
            var secondOrder = Persisted(dto.Id).Payment.ProviderOrderNumber;

            first.RedirectUrl.Should().Be("https://checkout.example.test/auth/token1");
            second.RedirectUrl.Should().Be("https://checkout.example.test/auth/token2");
            secondOrder.Should().NotBe(firstOrder);
            _gatewayCalls.Should().HaveCount(2).And.OnlyContain(c => c.StartsWith("/api/v1/session"));

            // A callback for the FIRST (superseded) session still lands on this payment.
            await CallbackAsync(SignedCallback(firstOrder!, "1.00", "USD", "Test product 1 USD"));
            Persisted(dto.Id).Payment.Status.Should().Be("Paid");
        }

        // ── Return-page status check (no callback received) ────────────
        private static HttpResponseMessage StatusJson(string status, string amount = "1.00", string currency = "USD") =>
            new(System.Net.HttpStatusCode.OK)
            {
                // Documented /api/v1/payment/status shape.
                Content = new StringContent(
                    $"{{\"payment_id\":\"c45ae9ac-be57-11f1-948e-02ae357f1548\",\"date\":\"2026-10-02 11:56:04\",\"status\":\"{status}\",\"reason\":null," +
                    $"\"order\":{{\"number\":\"x\",\"amount\":\"{amount}\",\"currency\":\"{currency}\",\"description\":\"Test product 1 USD\"}}}}"),
            };

        [Fact]
        public async Task Return_page_check_settles_a_payment_whose_callback_never_came()
        {
            var (id, on) = await RedirectedLinkAsync();
            _gateway = _ => StatusJson("settled");
            var code = Persisted(id).Payment.Code;

            var pub = await _svc.CheckPublicAsync(code);

            pub!.Status.Should().Be("Paid");
            var (p, ev) = Persisted(id);
            p.Status.Should().Be("Paid");
            p.IsFulfilled.Should().BeTrue();
            p.ProviderPaymentId.Should().Be("c45ae9ac-be57-11f1-948e-02ae357f1548");
            ev.Should().Contain(e => e.Kind == "status-check" && e.ResultStatus == "Paid");
            // Signed with order_id + password, as the docs require — never trusting the browser.
            _gatewayCalls.Should().ContainSingle().Which.Should()
                .Contain("/api/v1/payment/status").And.Contain($"\"order_id\":\"{on}\"")
                .And.Contain($"\"hash\":\"{MontyPayProvider.ComputeHash(on, SandboxPassword, false)}\"");
        }

        [Fact]
        public async Task Callback_arriving_after_the_return_page_check_is_logged_but_never_fulfils_twice()
        {
            var (id, on) = await RedirectedLinkAsync(10m, "EventTicket", referenceId: 42, description: "Gala - Entry Ticket #42");
            _events.Setup(e => e.MarkPaidByProviderRefAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()))
                   .ReturnsAsync(true);
            _gateway = _ => StatusJson("settled", amount: "10.00");

            (await _svc.CheckPublicAsync(Persisted(id).Payment.Code))!.Status.Should().Be("Paid");
            // MontyPay's own callback for the same payment lands afterwards.
            await CallbackAsync(SignedCallback(on, "10.00", "USD", "Gala - Entry Ticket #42", id: "c45ae9ac-be57-11f1-948e-02ae357f1548"));

            var (p, ev) = Persisted(id);
            p.Status.Should().Be("Paid");
            p.CallbackCount.Should().Be(1);
            ev.Should().Contain(e => e.Kind == "callback" && e.HashValid);
            ev.Count(e => e.Kind == "fulfilled").Should().Be(1);
            _events.Verify(e => e.MarkPaidByProviderRefAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task Return_page_check_is_silent_until_the_gateway_has_an_answer_and_is_throttled()
        {
            var (id, _) = await RedirectedLinkAsync();
            var code = Persisted(id).Payment.Code;   // gateway answers 404 "not found" (customer still on the card form)

            (await _svc.CheckPublicAsync(code))!.Status.Should().Be("Redirected");
            (await _svc.CheckPublicAsync(code))!.Status.Should().Be("Redirected");   // within 8 s → no gateway call

            _gatewayCalls.Should().HaveCount(1);
            Persisted(id).Events.Should().NotContain(e => e.Kind == "status-check");   // no event spam
        }

        [Fact]
        public async Task Return_page_check_holds_a_wrong_amount_instead_of_fulfilling()
        {
            var (id, _) = await RedirectedLinkAsync(25m);
            _gateway = _ => StatusJson("settled", amount: "1.00");
            var code = Persisted(id).Payment.Code;

            (await _svc.CheckPublicAsync(code))!.Status.Should().Be("Pending");
            var p = Persisted(id).Payment;
            p.IsFulfilled.Should().BeFalse();
            p.FailureReason.Should().Contain("NOT fulfilled");
        }

        [Fact]
        public async Task Return_page_check_reports_a_decline()
        {
            var (id, _) = await RedirectedLinkAsync();
            _gateway = _ => StatusJson("declined");

            (await _svc.CheckPublicAsync(Persisted(id).Payment.Code))!.Status.Should().Be("Failed");
        }

        [Fact]
        public async Task Return_page_check_never_calls_the_gateway_for_a_final_or_unstarted_payment()
        {
            var created = await _svc.CreateAsync(new OnlinePaymentCreateDto(1m, "never opened"), "admin");   // no session yet
            (await _svc.CheckPublicAsync(created.Code))!.Status.Should().Be("Created");

            var (id, on) = await RedirectedLinkAsync();
            await CallbackAsync(Settled(on));
            (await _svc.CheckPublicAsync(Persisted(id).Payment.Code))!.Status.Should().Be("Paid");

            _gatewayCalls.Should().BeEmpty();
            (await _svc.CheckPublicAsync("nope")).Should().BeNull();
        }

        [Fact]
        public async Task Paid_order_shows_its_tracking_page_after_payment()
        {
            _db.OnlineOrders.Add(new OnlineOrder
            {
                Id = 9, Code = "WEB-9", UserId = 1, CustomerName = "Test Buyer", CustomerPhone = "70000000",
                PaymentMode = "Online", Status = "AwaitingPayment", Subtotal = 30m, Total = 30m,
            });
            await _db.SaveChangesAsync();
            var (id, on) = await RedirectedLinkAsync(30m, "OnlineOrder", referenceId: 9, description: "AXIS order WEB-9");

            await CallbackAsync(SignedCallback(on, "30.00", "USD", "AXIS order WEB-9"));

            var pub = await _svc.GetPublicAsync(Persisted(id).Payment.Code);
            pub!.Status.Should().Be("Paid");
            pub.NextUrl.Should().Be("/orders/WEB-9");
        }
    }
}
