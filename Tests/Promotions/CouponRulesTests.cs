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
    public class CouponRulesTests : IDisposable
    {
        private static readonly DateTime Now = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

        private static Coupon Percent10(Action<Coupon>? tweak = null)
        {
            var c = new Coupon { Id = 1, Code = "TEN", Name = "Ten percent", Type = CouponType.Percent, Value = 10m, IsActive = true };
            tweak?.Invoke(c);
            return c;
        }

        // ── pure rules ───────────────────────────────────────────────────
        [Fact]
        public void Percent_coupon_discounts_the_order()
        {
            var ev = CouponEngine.Evaluate(Percent10(), 50m, null, 0, Now);
            ev.Eligible.Should().BeTrue();
            ev.DiscountAmount.Should().Be(5m);
        }

        [Fact]
        public void Inactive_coupon_is_refused()
        {
            CouponEngine.Evaluate(Percent10(c => c.IsActive = false), 50m, null, 0, Now).Eligible.Should().BeFalse();
        }

        [Fact]
        public void Validity_window_is_enforced_on_both_ends()
        {
            CouponEngine.Evaluate(Percent10(c => c.ValidFrom = Now.AddDays(1)), 50m, null, 0, Now).Reason.Should().Contain("not valid before");
            CouponEngine.Evaluate(Percent10(c => c.ValidTo = Now.AddDays(-1)), 50m, null, 0, Now).Reason.Should().Contain("expired");
            CouponEngine.Evaluate(Percent10(c => { c.ValidFrom = Now.AddDays(-1); c.ValidTo = Now.AddDays(1); }), 50m, null, 0, Now).Eligible.Should().BeTrue();
        }

        [Fact]
        public void Max_uses_and_per_customer_limits_are_enforced()
        {
            CouponEngine.Evaluate(Percent10(c => { c.MaxUses = 3; c.UsedCount = 3; }), 50m, null, 0, Now).Reason.Should().Contain("maximum number of uses");
            CouponEngine.Evaluate(Percent10(c => { c.MaxUses = 3; c.UsedCount = 2; }), 50m, null, 0, Now).Eligible.Should().BeTrue();
            CouponEngine.Evaluate(Percent10(c => c.MaxUsesPerCustomer = 1), 50m, null, customerUses: 1, Now).Reason.Should().Contain("customer");
            CouponEngine.Evaluate(Percent10(c => c.MaxUsesPerCustomer = 1), 50m, null, customerUses: 0, Now).Eligible.Should().BeTrue();
        }

        [Fact]
        public void Min_order_amount_is_enforced()
        {
            CouponEngine.Evaluate(Percent10(c => c.MinOrderAmount = 30m), 29.99m, null, 0, Now).Reason.Should().Contain("at least 30.00");
            CouponEngine.Evaluate(Percent10(c => c.MinOrderAmount = 30m), 30m, null, 0, Now).Eligible.Should().BeTrue();
        }

        [Fact]
        public void Category_restriction_limits_the_eligible_amount()
        {
            var cats = new Dictionary<int, decimal> { [1] = 10m, [2] = 40m }; // coffee 10, food 40
            var coffeeOnly = Percent10(c => c.CategoryIdsCsv = "1");
            var ev = CouponEngine.Evaluate(coffeeOnly, 50m, cats, 0, Now);
            ev.Eligible.Should().BeTrue();
            ev.EligibleAmount.Should().Be(10m);
            ev.DiscountAmount.Should().Be(1m);

            var noMatch = CouponEngine.Evaluate(Percent10(c => c.CategoryIdsCsv = "99"), 50m, cats, 0, Now);
            noMatch.Eligible.Should().BeFalse();
            noMatch.Reason.Should().Contain("categories");
        }

        [Fact]
        public void Percent_discount_is_capped_by_max_discount_amount()
        {
            var ev = CouponEngine.Evaluate(Percent10(c => { c.Value = 50m; c.MaxDiscountAmount = 7m; }), 100m, null, 0, Now);
            ev.DiscountAmount.Should().Be(7m);
        }

        [Fact]
        public void Fixed_coupon_never_exceeds_the_eligible_amount()
        {
            var fixed20 = Percent10(c => { c.Type = CouponType.Fixed; c.Value = 20m; });
            CouponEngine.Evaluate(fixed20, 12m, null, 0, Now).DiscountAmount.Should().Be(12m);
            CouponEngine.Evaluate(fixed20, 50m, null, 0, Now).DiscountAmount.Should().Be(20m);
        }

        // ── service against the open invoice ─────────────────────────────
        private readonly ApplicationDbContext _db = PromoDb.New();
        public void Dispose() => _db.Dispose();

        private CouponService Svc() => PromoAccounting.Coupons(_db);

        [Fact]
        public async Task Redeem_recomputes_from_the_open_invoice_applies_once_and_counts_the_use()
        {
            PromoDb.SeedCatalogAndInvoices(_db);
            // Food-only 50% coupon: invoice #10 has 2 lattes (10) + 1 burger (10) → eligible 10 → discount 5
            _db.Add(new Coupon { Id = 1, Code = "FOOD50", Name = "Half food", Type = CouponType.Percent, Value = 50m, CategoryIdsCsv = "2", IsActive = true });
            await _db.SaveChangesAsync();
            var svc = Svc();

            var validate = await svc.ValidateAsync(new CouponValidateDto("food50", TransactionId: 10));
            validate.Data!.Eligible.Should().BeTrue();
            validate.Data.DiscountAmount.Should().Be(5m);
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(20m, "validate must not apply");

            var redeem = await svc.RedeemAsync(new CouponRedeemDto(" food50 ", 10));
            redeem.Success.Should().BeTrue();
            redeem.Data!.InvoiceTotalAfter.Should().Be(15m);
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(15m);
            (await _db.Set<Coupon>().FindAsync(1))!.UsedCount.Should().Be(1);
            (await _db.Set<CouponRedemption>().CountAsync()).Should().Be(1);

            var again = await svc.RedeemAsync(new CouponRedeemDto("FOOD50", 10));
            again.Success.Should().BeFalse();
            again.Error.Should().Be("Already applied");
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(15m);

            var closed = await svc.RedeemAsync(new CouponRedeemDto("FOOD50", 20));
            closed.Success.Should().BeFalse();
        }
    }
}
