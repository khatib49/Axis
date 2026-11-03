using Application.Services.Payments2;
using Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Tests.Payments2
{
    public class WhishInvoicePaymentTests
    {
        [Fact]
        public async Task Start_refuses_when_whish_not_configured()
        {
            using var f = new PaymentsFixture();
            f.Whish.Setup(w => w.IsConfiguredAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
            f.AddTransaction(1, 20m);
            var r = await f.Payments.StartAsync(1, "Till", PaymentsFixture.Success, PaymentsFixture.Cancel);
            r.Success.Should().BeFalse();
            r.Error.Should().Be("NotConfigured");
            r.Message.Should().Contain("not configured");
        }

        [Fact]
        public async Task Start_refuses_closed_invoice()
        {
            using var f = new PaymentsFixture();
            f.AddTransaction(1, 20m, statusId: 6);
            var r = await f.Payments.StartAsync(1, "Till", PaymentsFixture.Success, PaymentsFixture.Cancel);
            r.Success.Should().BeFalse();
            r.Error.Should().Be("Closed");
            f.Whish.Verify(w => w.CreateCollectAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Start_refuses_zero_remaining()
        {
            using var f = new PaymentsFixture();
            f.AddTransaction(1, 20m);
            f.Paid[1] = 20m; // fully tendered in cash already
            var r = await f.Payments.StartAsync(1, "Till", PaymentsFixture.Success, PaymentsFixture.Cancel);
            r.Success.Should().BeFalse();
            r.Error.Should().Be("NothingToPay");
            (await f.Db.Set<WhishRequest>().CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task Start_creates_pending_request_for_remaining_amount_with_return_urls()
        {
            using var f = new PaymentsFixture();
            f.AddTransaction(5, 30m);
            f.Paid[5] = 10m;
            string? okUrl = null;
            f.Whish.Setup(w => w.CreateCollectAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string ext, decimal amt, string cur, string desc, string ok, string ko, CancellationToken _) => { okUrl = ok; return new WhishCollectResult(true, "https://whish.test/c/" + ext, null); });

            var r = await f.Payments.StartAsync(5, "SelfOrder", PaymentsFixture.Success, PaymentsFixture.Cancel, "/order/TBL-1");
            r.Success.Should().BeTrue();
            r.Data!.Amount.Should().Be(20m);
            r.Data.Currency.Should().Be("USD");
            r.Data.QrPayload.Should().Be(r.Data.CollectUrl);
            okUrl.Should().Be($"https://api.test/return?requestId={r.Data.RequestId}&result=success");

            var row = await f.Db.Set<WhishRequest>().SingleAsync();
            row.Status.Should().Be(WhishRequestStatuses.Pending);
            row.Source.Should().Be("SelfOrder");
            row.ReturnUrl.Should().Be("/order/TBL-1");
            row.ExternalId.Should().StartWith("inv-5-");

            // Starting again while the same pending request is fresh reuses it instead of creating a second collect.
            var again = await f.Payments.StartAsync(5, "SelfOrder", PaymentsFixture.Success, PaymentsFixture.Cancel);
            again.Data!.RequestId.Should().Be(r.Data.RequestId);
            (await f.Db.Set<WhishRequest>().CountAsync()).Should().Be(1);
        }

        [Fact]
        public async Task Start_marks_request_failed_when_provider_rejects()
        {
            using var f = new PaymentsFixture();
            f.AddTransaction(1, 20m);
            f.Whish.Setup(w => w.CreateCollectAsync(It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WhishCollectResult(false, null, "Whish error 401"));
            var r = await f.Payments.StartAsync(1, "Till", PaymentsFixture.Success, PaymentsFixture.Cancel);
            r.Success.Should().BeFalse();
            r.Message.Should().Be("Whish error 401");
            (await f.Db.Set<WhishRequest>().SingleAsync()).Status.Should().Be(WhishRequestStatuses.Failed);
        }

        [Fact]
        public async Task Check_marks_paid_once_records_one_tender_and_closes_when_fully_paid()
        {
            using var f = new PaymentsFixture();
            f.AddTransaction(1, 20m);
            var start = await f.Payments.StartAsync(1, "Kiosk", PaymentsFixture.Success, PaymentsFixture.Cancel);
            var id = start.Data!.RequestId;

            var pending = await f.Payments.CheckAsync(id);
            pending.Data!.Status.Should().Be(WhishRequestStatuses.Pending);
            f.TenderCalls.Should().BeEmpty();

            f.Whish.Setup(w => w.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WhishCollectStatus("success", "{\"data\":{\"collectStatus\":\"success\"}}"));
            var paid = await f.Payments.CheckAsync(id);
            paid.Data!.Status.Should().Be(WhishRequestStatuses.Paid);
            paid.Data.InvoiceClosed.Should().BeTrue();
            paid.Data.PaidOn.Should().NotBeNull();

            // Idempotent: further polls neither re-tender nor re-close nor re-ask Whish.
            await f.Payments.CheckAsync(id);
            await f.Payments.CheckAsync(id);
            f.TenderCalls.Should().HaveCount(1);
            var line = f.TenderCalls[0].Tenders.Single();
            line.Amount.Should().Be(20m);
            line.CurrencyCode.Should().Be("USD");
            line.Method.Should().Be(WhishInvoicePaymentService.TenderMethod);
            line.Reference.Should().StartWith(WhishInvoicePaymentService.ReferencePrefix + "inv-1-");
            f.Transactions.Verify(t => t.CloseOpenInvoice(1, It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<decimal>()), Times.Once);
            f.Whish.Verify(w => w.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
            (await f.Db.Set<WhishRequest>().SingleAsync()).RawCallback.Should().Contain("success");
        }

        [Fact]
        public async Task Check_does_not_close_invoice_when_partially_paid()
        {
            using var f = new PaymentsFixture();
            f.AddTransaction(2, 50m);
            var start = await f.Payments.StartAsync(2, "Till", PaymentsFixture.Success, PaymentsFixture.Cancel);
            // A cash tender of 10 is recorded after the Whish request was raised for 50 — Whish pays 50, nothing remains.
            // Simulate instead the opposite: the request was raised for 50 but the invoice total grew to 60 before payment.
            var tx = await f.Db.Set<TransactionRecord>().FirstAsync(t => t.Id == 2);
            tx.TotalPrice = 60m; await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();

            f.Whish.Setup(w => w.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WhishCollectStatus("success", null));
            var r = await f.Payments.CheckAsync(start.Data!.RequestId);
            r.Data!.Status.Should().Be(WhishRequestStatuses.Paid);
            r.Data.InvoiceClosed.Should().BeFalse();
            f.TenderCalls.Should().HaveCount(1);
            f.Transactions.Verify(t => t.CloseOpenInvoice(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<decimal>()), Times.Never);
        }

        [Fact]
        public async Task Check_marks_failed_and_expired()
        {
            using var f = new PaymentsFixture();
            f.AddTransaction(1, 20m);
            f.AddTransaction(2, 20m);
            var a = (await f.Payments.StartAsync(1, "Till", PaymentsFixture.Success, PaymentsFixture.Cancel)).Data!.RequestId;
            var b = (await f.Payments.StartAsync(2, "Till", PaymentsFixture.Success, PaymentsFixture.Cancel)).Data!.RequestId;

            f.Whish.Setup(w => w.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WhishCollectStatus("failed", null));
            (await f.Payments.CheckAsync(a)).Data!.Status.Should().Be(WhishRequestStatuses.Failed);

            f.Whish.Setup(w => w.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new WhishCollectStatus("pending", null));
            var old = await f.Db.Set<WhishRequest>().FirstAsync(r => r.Id == b);
            old.CreatedOn = DateTime.UtcNow.AddHours(-2); await f.Db.SaveChangesAsync();
            (await f.Payments.CheckAsync(b)).Data!.Status.Should().Be(WhishRequestStatuses.Expired);
            f.TenderCalls.Should().BeEmpty();
        }
    }
}
