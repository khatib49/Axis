using Application.Services.Promotions;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Promotions
{
    /// <summary>The one permitted write to transactions.TotalPrice must be tightly guarded.</summary>
    public class InvoiceAdjusterTests : IDisposable
    {
        private readonly ApplicationDbContext _db = PromoDb.New();
        private readonly PromotionInvoiceAdjuster _adj;

        public InvoiceAdjusterTests()
        {
            PromoDb.SeedCatalogAndInvoices(_db);
            _adj = new PromotionInvoiceAdjuster(_db, new FakeUser(), NullLogger<PromotionInvoiceAdjuster>.Instance);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Applies_delta_to_open_invoice_and_writes_audit_row()
        {
            var r = await _adj.ApplyAsync(10, -5m, "coupon:TEST", "test", default);
            await _db.SaveChangesAsync();

            r.Success.Should().BeTrue();
            r.OldTotal.Should().Be(20m);
            r.NewTotal.Should().Be(15m);
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(15m);

            var log = await _db.TransactionAuditLogs.SingleAsync(a => a.TransactionId == 10);
            log.Action.Should().Be("PromotionAdjust");
            log.FieldChanged.Should().Be("TotalPrice");
            log.OldValue.Should().Be("20.00");
            log.NewValue.Should().Be("15.00");
            log.Notes.Should().StartWith("[coupon:TEST]");
            log.ChangedBy.Should().Be("tester@axislb.com");
        }

        [Fact]
        public async Task Refuses_closed_invoice()
        {
            var r = await _adj.ApplyAsync(20, -5m, "coupon:TEST", "test", default);
            r.Success.Should().BeFalse();
            r.Error.Should().Contain("open invoice");
            (await _db.Transactions.FindAsync(20))!.TotalPrice.Should().Be(15m);
            _db.ChangeTracker.Entries<TransactionAuditLog>().Should().BeEmpty();
        }

        [Fact]
        public async Task Refuses_missing_invoice()
        {
            var r = await _adj.ApplyAsync(999, -5m, "x", "test", default);
            r.Success.Should().BeFalse();
        }

        [Fact]
        public async Task Never_takes_total_below_zero()
        {
            var r = await _adj.ApplyAsync(10, -20.01m, "giftcard:X", "test", default);
            r.Success.Should().BeFalse();
            r.Error.Should().Contain("below zero");
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(20m);

            var exact = await _adj.ApplyAsync(10, -20m, "giftcard:Y", "test", default);
            exact.Success.Should().BeTrue();
            exact.NewTotal.Should().Be(0m);
        }

        [Fact]
        public async Task Same_key_is_applied_only_once_per_invoice_pending_or_saved()
        {
            var first = await _adj.ApplyAsync(10, -2m, "happyhour:tx10", "a", default);
            first.Success.Should().BeTrue();

            // Still pending in the change tracker
            var pending = await _adj.ApplyAsync(10, -2m, "happyhour:tx10", "b", default);
            pending.Success.Should().BeFalse();
            pending.AlreadyApplied.Should().BeTrue();

            await _db.SaveChangesAsync();

            // Saved
            var saved = await _adj.ApplyAsync(10, -2m, "happyhour:tx10", "c", default);
            saved.Success.Should().BeFalse();
            saved.AlreadyApplied.Should().BeTrue();

            // Different key on the same invoice is fine
            var other = await _adj.ApplyAsync(10, -2m, "coupon:OTHER", "d", default);
            other.Success.Should().BeTrue();
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(16m);
        }

        [Fact]
        public async Task Zero_delta_and_blank_key_are_rejected()
        {
            (await _adj.ApplyAsync(10, 0m, "k", "n", default)).Success.Should().BeFalse();
            (await _adj.ApplyAsync(10, -1m, " ", "n", default)).Success.Should().BeFalse();
        }
    }
}
