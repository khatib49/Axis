using System.Security.Cryptography;
using System.Text;
using Application.DTOs;
using Application.IServices;
using Application.Services.Phase4;
using Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Tests.Payments2;
using Xunit;

namespace Tests.Phase4
{
    public class CardTerminalServiceTests : IDisposable
    {
        private readonly PaymentsFixture _f = new();
        private readonly Mock<IIntegrationSettingsService> _settings = new();
        private readonly CardTerminalService _svc;

        public CardTerminalServiceTests()
        {
            _settings.Setup(s => s.GetRawAsync(SimulatorTerminalProvider.AllowKey, It.IsAny<CancellationToken>())).ReturnsAsync("true");
            _settings.Setup(s => s.GetRawAsync(CardTerminalService.CallbackSecretKey, It.IsAny<CancellationToken>())).ReturnsAsync("cb-secret");
            var providers = new ICardTerminalProvider[]
            {
                new ManualTerminalProvider(), new SimulatorTerminalProvider(_settings.Object),
                new CloudHttpTerminalProvider(_settings.Object, Mock.Of<IHttpClientFactory>()),
            };
            _svc = new CardTerminalService(_f.Db, providers, _f.Tenders.Object, _f.Transactions.Object, _settings.Object, _f.User, NullLogger<CardTerminalService>.Instance);
            _f.Db.Add(new CardTerminal { Id = 1, Name = "Sim", Provider = CardTerminalProviders.Simulator });
            _f.Db.Add(new CardTerminal { Id = 2, Name = "Bank POS", Provider = CardTerminalProviders.Manual });
            _f.Db.SaveChanges();
        }

        public void Dispose() => _f.Dispose();

        private void Age(int paymentId, int seconds)
        {
            var p = _f.Db.Set<TerminalPayment>().First(x => x.Id == paymentId);
            p.CreatedOn = DateTime.UtcNow.AddSeconds(-seconds);
            _f.Db.SaveChanges();
        }

        [Fact]
        public async Task Approved_payment_records_one_card_tender_and_closes_the_invoice()
        {
            _f.AddTransaction(10, 25m);
            var start = await _svc.StartAsync(new TerminalStartDto(10, 1));
            start.Success.Should().BeTrue(start.Message);
            start.Data!.Status.Should().Be("Pending");
            start.Data.Amount.Should().Be(25m);

            (await _svc.CheckAsync(start.Data.Id)).Data!.Status.Should().Be("Pending"); // simulator waits 3 s
            Age(start.Data.Id, 5);
            var done = await _svc.CheckAsync(start.Data.Id);
            done.Data!.Status.Should().Be("Approved");
            done.Data.CardLast4.Should().Be("4242");
            done.Data.ApprovalCode.Should().NotBeNullOrEmpty();
            done.Data.InvoiceClosed.Should().BeTrue();

            await _svc.CheckAsync(start.Data.Id); // polling again must not add a second tender
            _f.TenderCalls.Should().ContainSingle();
            _f.TenderCalls[0].Tenders.Single().Method.Should().Be("Card");
            _f.TenderCalls[0].Tenders.Single().Reference.Should().StartWith("Card:axis-10-");
        }

        [Fact]
        public async Task Partial_card_amount_leaves_the_invoice_open()
        {
            _f.AddTransaction(11, 30m);
            var start = await _svc.StartAsync(new TerminalStartDto(11, 1, 10m));
            Age(start.Data!.Id, 5);
            var done = await _svc.CheckAsync(start.Data.Id);
            done.Data!.Status.Should().Be("Approved");
            done.Data.InvoiceClosed.Should().BeFalse();
            _f.Paid[11].Should().Be(10m);
        }

        [Fact]
        public async Task Declined_payment_records_nothing()
        {
            _f.AddTransaction(12, 20.13m);
            var start = await _svc.StartAsync(new TerminalStartDto(12, 1));
            Age(start.Data!.Id, 5);
            (await _svc.CheckAsync(start.Data.Id)).Data!.Status.Should().Be("Declined");
            _f.TenderCalls.Should().BeEmpty();
        }

        [Fact]
        public async Task Simulator_refuses_to_run_unless_allowed()
        {
            _settings.Setup(s => s.GetRawAsync(SimulatorTerminalProvider.AllowKey, It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
            _f.AddTransaction(13, 5m);
            var r = await _svc.StartAsync(new TerminalStartDto(13, 1));
            r.Success.Should().BeFalse();
            r.Data!.Status.Should().Be("Failed");
            r.Message.Should().Contain("disabled");
        }

        [Fact]
        public async Task Start_validates_invoice_amount_and_busy_terminal()
        {
            _f.AddTransaction(14, 20m);
            _f.AddTransaction(15, 20m);
            _f.AddTransaction(16, 20m, statusId: 6);
            (await _svc.StartAsync(new TerminalStartDto(16, 1))).Error.Should().Be("Closed");
            (await _svc.StartAsync(new TerminalStartDto(14, 1, 25m))).Error.Should().Be("Validation");
            (await _svc.StartAsync(new TerminalStartDto(14, 99))).Error.Should().Be("NotFound");
            var first = await _svc.StartAsync(new TerminalStartDto(14, 1));
            first.Success.Should().BeTrue();
            (await _svc.StartAsync(new TerminalStartDto(15, 1))).Error.Should().Be("Busy");
            var again = await _svc.StartAsync(new TerminalStartDto(14, 1));
            again.Success.Should().BeTrue();
            again.Data!.Id.Should().Be(first.Data!.Id);
            (await _svc.CancelAsync(first.Data.Id)).Data!.Status.Should().Be("Cancelled");
            (await _svc.StartAsync(new TerminalStartDto(15, 1))).Success.Should().BeTrue();
        }

        [Fact]
        public async Task Manual_terminal_waits_for_the_approval_code()
        {
            _f.AddTransaction(20, 18m);
            var start = await _svc.StartAsync(new TerminalStartDto(20, 2));
            start.Data!.Status.Should().Be("Pending");
            (await _svc.CheckAsync(start.Data.Id)).Data!.Status.Should().Be("Pending");
            (await _svc.ConfirmManualAsync(start.Data.Id, new TerminalManualConfirmDto("!", null))).Error.Should().Be("Validation");
            (await _svc.ConfirmManualAsync(start.Data.Id, new TerminalManualConfirmDto("A1B2C3", "12"))).Error.Should().Be("Validation");
            var ok = await _svc.ConfirmManualAsync(start.Data.Id, new TerminalManualConfirmDto("a1b2c3", "1234", "mastercard"));
            ok.Data!.Status.Should().Be("Approved");
            ok.Data.ApprovalCode.Should().Be("A1B2C3");
            ok.Data.CardScheme.Should().Be("MASTERCARD");
            ok.Data.InvoiceClosed.Should().BeTrue();
            (await _svc.ConfirmManualAsync(start.Data.Id, new TerminalManualConfirmDto("X1", "1234"))).Error.Should().Be("Closed");
        }

        [Fact]
        public async Task Pending_payment_expires_when_the_terminal_never_answers()
        {
            _f.AddTransaction(21, 9m);
            _f.Db.Add(new CardTerminal { Id = 3, Name = "Cloud", Provider = CardTerminalProviders.CloudHttp, ProviderTerminalId = "T1" });
            _f.Db.SaveChanges();
            // Cloud start fails without a base URL, so seed a pending payment directly.
            _f.Db.Add(new TerminalPayment { Id = 50, TransactionId = 21, TerminalId = 3, Amount = 9m, ExternalId = "ext-50", Status = "Pending", CreatedOn = DateTime.UtcNow.AddMinutes(-10) });
            _f.Db.SaveChanges();
            (await _svc.CheckAsync(50)).Data!.Status.Should().Be("Expired");
        }

        [Fact]
        public async Task Callback_requires_a_valid_signature_and_approves_the_payment()
        {
            _f.AddTransaction(30, 12m);
            _f.Db.Add(new CardTerminal { Id = 4, Name = "Cloud", Provider = CardTerminalProviders.CloudHttp, ProviderTerminalId = "T9" });
            _f.Db.Add(new TerminalPayment { Id = 60, TransactionId = 30, TerminalId = 4, Amount = 12m, ExternalId = "ext-60", Status = "Pending", CreatedOn = DateTime.UtcNow });
            _f.Db.SaveChanges();
            var body = "{\"reference\":\"ext-60\",\"status\":\"approved\",\"approvalCode\":\"778899\",\"last4\":\"**** 9876\",\"scheme\":\"VISA\"}";
            var dto = new TerminalCallbackDto("ext-60", "approved", "778899", "**** 9876", "VISA");
            (await _svc.CallbackAsync("sha256=bad", body, dto)).Error.Should().Be("Unauthorized");
            var sig = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("cb-secret"), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
            var ok = await _svc.CallbackAsync(sig, body, dto);
            ok.Success.Should().BeTrue(ok.Message);
            ok.Data!.Status.Should().Be("Approved");
            ok.Data.CardLast4.Should().Be("9876");
            ok.Data.InvoiceClosed.Should().BeTrue();
        }

        [Fact]
        public async Task Report_totals_approved_payments_by_terminal_and_scheme()
        {
            _f.AddTransaction(40, 10m);
            var p = await _svc.StartAsync(new TerminalStartDto(40, 2));
            await _svc.ConfirmManualAsync(p.Data!.Id, new TerminalManualConfirmDto("OK1", "1111", "VISA"));
            var rep = await _svc.ReportAsync(DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1), null);
            rep.Approved.Should().Be(1);
            rep.ApprovedAmount.Should().Be(10m);
            rep.ByScheme.Single().Scheme.Should().Be("VISA");
            rep.ByTerminal.Single().TerminalName.Should().Be("Bank POS");
        }

        [Fact]
        public void CloudHttp_parser_maps_provider_words()
        {
            CloudHttpTerminalProvider.Parse("{\"status\":\"captured\",\"last4\":\"4111111111111111\"}").Should().Be(new TerminalProviderResult("Approved", null, "1111", null, null, null));
            CloudHttpTerminalProvider.MapStatus("REJECTED").Should().Be("Declined");
            CloudHttpTerminalProvider.MapStatus("weird").Should().Be("Pending");
            CloudHttpTerminalProvider.Parse("not json").Status.Should().Be("Pending");
        }

        [Fact]
        public async Task Terminal_with_history_is_deactivated_not_deleted()
        {
            _f.AddTransaction(41, 10m);
            await _svc.StartAsync(new TerminalStartDto(41, 2));
            var r = await _svc.DeleteTerminalAsync(2);
            r.Data.Should().BeFalse();
            (await _f.Db.Set<CardTerminal>().AsNoTracking().SingleAsync(t => t.Id == 2)).IsActive.Should().BeFalse();
            (await _svc.CreateTerminalAsync(new CardTerminalUpsertDto("X", "CloudHttp"))).Error.Should().Be("Validation");
        }
    }
}
