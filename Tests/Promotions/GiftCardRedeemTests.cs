using Application.DTOs;
using Application.Services.Promotions;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Promotions
{
    public class GiftCardRedeemTests : IDisposable
    {
        private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

        private static GiftCard Card(decimal balance, Action<GiftCard>? tweak = null)
        {
            var c = new GiftCard { Id = 1, Code = "ABCDEFGHJKLMNPQR", InitialAmount = balance, Balance = balance, Status = GiftCardStatus.Active };
            tweak?.Invoke(c);
            return c;
        }

        [Fact]
        public void Generated_codes_are_16_unambiguous_uppercase_chars()
        {
            for (var i = 0; i < 50; i++)
            {
                var code = GiftCardService.GenerateCode();
                code.Should().HaveLength(16);
                code.Should().MatchRegex("^[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{16}$");
            }
        }

        [Fact]
        public void Redeem_rules_balance_expiry_status()
        {
            GiftCardService.CanRedeem(Card(50m), 20m, Now).Ok.Should().BeTrue();
            GiftCardService.CanRedeem(Card(50m), 50m, Now).Ok.Should().BeTrue();
            GiftCardService.CanRedeem(Card(50m), 50.01m, Now).Reason.Should().Contain("Insufficient");
            GiftCardService.CanRedeem(Card(50m), 0m, Now).Reason.Should().Contain("greater than zero");
            GiftCardService.CanRedeem(Card(0m), 1m, Now).Reason.Should().Contain("no remaining balance");
            GiftCardService.CanRedeem(Card(50m, c => c.ExpiresOn = Now.AddMinutes(-1)), 1m, Now).Reason.Should().Contain("expired");
            GiftCardService.CanRedeem(Card(50m, c => c.ExpiresOn = Now.AddDays(1)), 1m, Now).Ok.Should().BeTrue();
            GiftCardService.CanRedeem(Card(50m, c => c.Status = GiftCardStatus.Disabled), 1m, Now).Reason.Should().Contain("disabled");
        }

        private readonly ApplicationDbContext _db = PromoDb.New();
        public void Dispose() => _db.Dispose();

        private GiftCardService Svc()
            => new(_db, new PromotionInvoiceAdjuster(_db, new FakeUser(), NullLogger<PromotionInvoiceAdjuster>.Instance),
                new FakeUser(), NullLogger<GiftCardService>.Instance);

        [Fact]
        public async Task Redeem_debits_card_writes_ledger_and_reduces_open_invoice()
        {
            PromoDb.SeedCatalogAndInvoices(_db);
            _db.Add(Card(30m));
            await _db.SaveChangesAsync();
            var svc = Svc();

            var r = await svc.RedeemAsync(new GiftCardRedeemDto("abcd-efgh jklm npqr", 10, 12.5m));
            r.Success.Should().BeTrue(r.Message);
            r.Data!.AppliedAsPayment.Should().Be(12.5m);
            r.Data.RemainingBalance.Should().Be(17.5m);
            r.Data.InvoiceTotalAfter.Should().Be(7.5m);

            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(7.5m);
            var ledger = await _db.Set<GiftCardTransaction>().Where(t => t.GiftCardId == 1 && t.Type == "Redeem").SingleAsync();
            ledger.Amount.Should().Be(12.5m);
            ledger.TransactionId.Should().Be(10);
            ledger.BalanceAfter.Should().Be(17.5m);
            (await _db.TransactionAuditLogs.CountAsync(a => a.TransactionId == 10 && a.Action == "PromotionAdjust")).Should().Be(1);
        }

        [Fact]
        public async Task Redeem_cannot_exceed_invoice_total_or_card_balance_and_needs_an_open_invoice()
        {
            PromoDb.SeedCatalogAndInvoices(_db);
            _db.Add(Card(100m));
            await _db.SaveChangesAsync();
            var svc = Svc();

            (await svc.RedeemAsync(new GiftCardRedeemDto("ABCDEFGHJKLMNPQR", 10, 20.01m))).Message.Should().Contain("left to pay");
            (await svc.RedeemAsync(new GiftCardRedeemDto("ABCDEFGHJKLMNPQR", 10, 100.01m))).Message.Should().Contain("Insufficient");
            (await svc.RedeemAsync(new GiftCardRedeemDto("ABCDEFGHJKLMNPQR", 20, 5m))).Message.Should().Contain("open invoice");
            (await svc.RedeemAsync(new GiftCardRedeemDto("NOPE", 10, 5m))).Error.Should().Be("Not found");

            (await _db.Set<GiftCard>().FindAsync(1))!.Balance.Should().Be(100m);
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(20m);
        }

        [Fact]
        public async Task Card_becomes_depleted_when_fully_used_and_refund_restores_it()
        {
            PromoDb.SeedCatalogAndInvoices(_db);
            _db.Add(Card(20m));
            await _db.SaveChangesAsync();
            var svc = Svc();

            var r = await svc.RedeemAsync(new GiftCardRedeemDto("ABCDEFGHJKLMNPQR", 10, 20m));
            r.Success.Should().BeTrue();
            r.Data!.Status.Should().Be(GiftCardStatus.Depleted);
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(0m);

            var lookup = await svc.LookupAsync("ABCDEFGHJKLMNPQR");
            lookup.Data!.CanRedeem.Should().BeFalse();

            var refund = await svc.RefundAsync(1, new GiftCardRefundDto(5m, 10, "partial void"));
            refund.Success.Should().BeTrue(refund.Message);
            refund.Data!.Balance.Should().Be(5m);
            refund.Data.Status.Should().Be(GiftCardStatus.Active);

            (await svc.RefundAsync(1, new GiftCardRefundDto(16m, 10, null))).Message.Should().Contain("Only 15.00");
        }
    }
}
