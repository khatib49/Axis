using Application.Services.Promotions;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Promotions
{
    /// <summary>
    /// Phase 2: happy hour + combos apply automatically, idempotently, and are
    /// re-evaluated (delta only) when the invoice lines change.
    /// </summary>
    public class AutoApplyTests : IDisposable
    {
        private readonly ApplicationDbContext _db = PromoDb.New();
        private readonly PromotionAutoApplyService _svc;

        public AutoApplyTests()
        {
            PromoDb.SeedCatalogAndInvoices(_db); // invoice #10: 2 lattes (5) + 1 burger (10) = 20; #20 paid
            _svc = new PromotionAutoApplyService(_db,
                new PromotionInvoiceAdjuster(_db, new FakeUser(), NullLogger<PromotionInvoiceAdjuster>.Instance),
                new FakeUser(), NullLogger<PromotionAutoApplyService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        /// <summary>Always-on rule (StartTime == EndTime, no weekday filter) so the test does not depend on the clock.</summary>
        private async Task CoffeeHalfPriceAsync(int id = 1, string? categories = "1", decimal pct = 50m)
        {
            _db.Add(new HappyHourRule { Id = id, Name = "Coffee hour", DaysOfWeekCsv = "", StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(0, 0), DiscountPercent = pct, CategoryIdsCsv = categories, IsActive = true });
            await _db.SaveChangesAsync();
        }

        private async Task<Combo> BurgerMealAsync(decimal price = 15m, int burgers = 1, int fries = 2, int id = 1, bool active = true)
        {
            // burger 10 + 2 fries 8 = 18 per set
            var c = new Combo { Id = id, Name = "Burger meal", Price = price, IsActive = active };
            c.Items.Add(new ComboItem { ItemId = 2, Quantity = burgers });
            c.Items.Add(new ComboItem { ItemId = 3, Quantity = fries });
            _db.Add(c);
            await _db.SaveChangesAsync();
            return c;
        }

        /// <summary>Mimic the existing order path: lines land at full price and the total grows / shrinks.</summary>
        private async Task SetLineAsync(int txId, int itemId, int qty)
        {
            var trx = await _db.Transactions.Include(t => t.TransactionItems).SingleAsync(t => t.Id == txId);
            var price = (await _db.Items.FindAsync(itemId))!.Price;
            var line = trx.TransactionItems.FirstOrDefault(l => l.ItemId == itemId);
            var oldQty = line?.Quantity ?? 0;
            if (line is null) trx.TransactionItems.Add(new TransactionItem { TransactionRecordId = txId, ItemId = itemId, Quantity = qty });
            else if (qty == 0) { trx.TransactionItems.Remove(line); _db.Remove(line); }
            else line.Quantity = qty;
            trx.TotalPrice += (qty - oldQty) * price;
            await _db.SaveChangesAsync();
        }

        private async Task<decimal> TotalAsync(int txId = 10) => (await _db.Transactions.AsNoTracking().SingleAsync(t => t.Id == txId)).TotalPrice;
        private Task<int> AuditCountAsync(int txId = 10) => _db.TransactionAuditLogs.CountAsync(a => a.TransactionId == txId && a.Action == PromotionInvoiceAdjuster.AuditAction);

        [Fact]
        public async Task Happy_hour_is_applied_then_re_evaluated_as_lines_are_added_and_removed()
        {
            await CoffeeHalfPriceAsync();

            var first = await _svc.ApplyAsync(10);
            first.Success.Should().BeTrue(first.Message);
            first.Data!.HappyHour!.Delta.Should().Be(-5m);
            first.Data.HappyHour.Amount.Should().Be(5m);
            first.Data.TotalBefore.Should().Be(20m);
            first.Data.TotalAfter.Should().Be(15m);
            (await TotalAsync()).Should().Be(15m);
            var app = await _db.Set<HappyHourApplication>().SingleAsync(a => a.TransactionId == 10);
            app.Amount.Should().Be(5m); app.Version.Should().Be(1); app.RuleId.Should().Be(1);

            // Two more lattes (+10 at full price) → discount should grow by 5.
            await SetLineAsync(10, 1, 4);
            (await TotalAsync()).Should().Be(25m);
            var more = await _svc.ApplyAsync(10);
            more.Data!.HappyHour!.Delta.Should().Be(-5m);
            more.Data.HappyHour.Amount.Should().Be(10m);
            (await TotalAsync()).Should().Be(20m);
            (await _db.Set<HappyHourApplication>().SingleAsync(a => a.TransactionId == 10)).Version.Should().Be(2);

            // Down to one latte (−15 at full price) → 7.50 of discount is given back.
            await SetLineAsync(10, 1, 1);
            (await TotalAsync()).Should().Be(5m);
            var fewer = await _svc.ApplyAsync(10);
            fewer.Data!.HappyHour!.Delta.Should().Be(7.5m);
            fewer.Data.HappyHour.Amount.Should().Be(2.5m);
            (await TotalAsync()).Should().Be(12.5m);

            // Unique TransactionId kept: still one application row, three audit rows (one per delta).
            (await _db.Set<HappyHourApplication>().CountAsync(a => a.TransactionId == 10)).Should().Be(1);
            (await AuditCountAsync()).Should().Be(3);
            var notes = await _db.TransactionAuditLogs.Where(a => a.TransactionId == 10).Select(a => a.Notes!).ToListAsync();
            notes.Should().Contain(n => n.StartsWith("[happyhour:tx10:v1]"))
                 .And.Contain(n => n.StartsWith("[happyhour:tx10:v2]"))
                 .And.Contain(n => n.StartsWith("[happyhour:tx10:v3]"));
        }

        [Fact]
        public async Task Combo_sets_are_counted_from_mixed_quantities_and_re_evaluated()
        {
            await BurgerMealAsync(); // 1 burger + 2 fries for 15 (parts 18, saving 3 per set)

            // 2 lattes + 1 burger, no fries → no set.
            var none = await _svc.ApplyAsync(10);
            none.Success.Should().BeTrue();
            none.Data!.Combos.Should().BeEmpty();
            (await TotalAsync()).Should().Be(20m);

            // + 3 fries (12): 1 burger / 3 fries → floor(1/1)=1, floor(3/2)=1 → ONE set.
            await SetLineAsync(10, 3, 3);
            var one = await _svc.ApplyAsync(10);
            one.Data!.Combos.Should().ContainSingle();
            one.Data.Combos[0].Quantity.Should().Be(1);
            one.Data.Combos[0].Amount.Should().Be(3m);
            one.Data.Combos[0].Delta.Should().Be(-3m);
            (await TotalAsync()).Should().Be(32m - 3m);

            // 3 burgers / 5 fries → min(3, 2) = TWO sets → saving 6, delta −3.
            await SetLineAsync(10, 2, 3);
            await SetLineAsync(10, 3, 5);
            var two = await _svc.ApplyAsync(10);
            two.Data!.Combos[0].Quantity.Should().Be(2);
            two.Data.Combos[0].Amount.Should().Be(6m);
            two.Data.Combos[0].Delta.Should().Be(-3m);
            (await TotalAsync()).Should().Be(10m + 30m + 20m - 6m);

            // Fries removed → no set → the 6.00 is refunded, row stays at 0.
            await SetLineAsync(10, 3, 0);
            var gone = await _svc.ApplyAsync(10);
            gone.Data!.Combos.Should().ContainSingle(c => c.Delta == 6m && c.Amount == 0m && c.Quantity == 0);
            (await TotalAsync()).Should().Be(40m);

            var rows = await _db.Set<ComboApplication>().Where(a => a.TransactionId == 10).ToListAsync();
            rows.Should().ContainSingle();
            rows[0].Version.Should().Be(3);
            var notes = await _db.TransactionAuditLogs.Where(a => a.TransactionId == 10).Select(a => a.Notes!).ToListAsync();
            notes.Should().Contain(n => n.StartsWith("[combo:1:tx10:v1]")).And.Contain(n => n.StartsWith("[combo:1:tx10:v3]"));
        }

        [Fact]
        public async Task Overlapping_combos_never_count_the_same_line_twice()
        {
            await BurgerMealAsync(price: 15m, burgers: 1, fries: 2, id: 1);          // saving 3
            await BurgerMealAsync(price: 13m, burgers: 1, fries: 1, id: 2);          // parts 14, saving 1
            await SetLineAsync(10, 3, 2);                                           // 1 burger + 2 fries on the invoice

            var r = await _svc.ApplyAsync(10);
            r.Data!.Combos.Should().ContainSingle(c => c.ComboId == 1 && c.Quantity == 1 && c.Amount == 3m,
                "the single burger goes to the combo with the bigger saving and is not reused");
            (await TotalAsync()).Should().Be(28m - 3m);
        }

        [Fact]
        public async Task Repeated_calls_are_idempotent()
        {
            await CoffeeHalfPriceAsync();
            await BurgerMealAsync();
            await SetLineAsync(10, 3, 2); // 2 lattes + burger + 2 fries = 28 → HH 5, combo 3

            var first = await _svc.ApplyAsync(10);
            first.Data!.TotalAfter.Should().Be(20m);

            for (var i = 0; i < 3; i++)
            {
                var again = await _svc.ApplyAsync(10);
                again.Success.Should().BeTrue();
                again.Data!.HappyHour!.Delta.Should().Be(0m);
                again.Data.HappyHour.Amount.Should().Be(5m);
                again.Data.Combos.Should().ContainSingle(c => c.Delta == 0m && c.Amount == 3m);
                again.Data.TotalBefore.Should().Be(20m);
                again.Data.TotalAfter.Should().Be(20m);
            }
            (await TotalAsync()).Should().Be(20m);
            (await AuditCountAsync()).Should().Be(2);
            (await _db.Set<HappyHourApplication>().SingleAsync()).Version.Should().Be(1);
            (await _db.Set<ComboApplication>().SingleAsync()).Version.Should().Be(1);
        }

        [Fact]
        public async Task Closed_or_missing_invoice_is_refused_without_touching_anything()
        {
            await CoffeeHalfPriceAsync();
            var closed = await _svc.ApplyAsync(20);
            closed.Success.Should().BeFalse();
            closed.Error.Should().Be("Invoice not open");
            closed.Message.Should().Contain("not open");
            (await TotalAsync(20)).Should().Be(15m);
            (await _svc.ApplyAsync(999)).Success.Should().BeFalse();
            (await _db.Set<HappyHourApplication>().AnyAsync()).Should().BeFalse();
            (await AuditCountAsync(20)).Should().Be(0);
        }

        [Fact]
        public async Task Never_takes_the_invoice_below_zero()
        {
            // Something else (a coupon) already took the invoice down to 3.
            var trx = await _db.Transactions.SingleAsync(t => t.Id == 10);
            trx.TotalPrice = 3m;
            await _db.SaveChangesAsync();
            await CoffeeHalfPriceAsync(categories: null, pct: 100m); // wants 20 off

            var r = await _svc.ApplyAsync(10);
            r.Success.Should().BeTrue(r.Message);
            r.Data!.HappyHour!.Delta.Should().Be(-3m);
            r.Data.HappyHour.Amount.Should().Be(3m, "only what is left on the invoice can be discounted");
            r.Data.TotalAfter.Should().Be(0m);
            (await TotalAsync()).Should().Be(0m);

            // Calling again at 0 changes nothing.
            var again = await _svc.ApplyAsync(10);
            again.Data!.HappyHour!.Delta.Should().Be(0m);
            (await TotalAsync()).Should().Be(0m);
        }

        [Fact]
        public async Task No_op_when_nothing_matches_and_earlier_happy_hour_is_kept_once_the_window_closed()
        {
            // No rules, no combos → success with zero deltas.
            var idle = await _svc.ApplyAsync(10);
            idle.Success.Should().BeTrue();
            idle.Data!.HappyHour.Should().BeNull();
            idle.Data.Combos.Should().BeEmpty();
            idle.Data.TotalBefore.Should().Be(idle.Data.TotalAfter);

            // Discount booked while the rule was on, then the rule is switched off → kept, not clawed back.
            await CoffeeHalfPriceAsync();
            (await _svc.ApplyAsync(10)).Data!.TotalAfter.Should().Be(15m);
            var rule = await _db.Set<HappyHourRule>().SingleAsync();
            rule.IsActive = false;
            await _db.SaveChangesAsync();

            var later = await _svc.ApplyAsync(10);
            later.Data!.HappyHour!.Delta.Should().Be(0m);
            later.Data.HappyHour.Amount.Should().Be(5m);
            later.Data.Messages.Should().ContainSingle(m => m.Contains("kept"));
            (await TotalAsync()).Should().Be(15m);
        }

        [Fact]
        public async Task GetApplied_reports_happy_hour_combos_and_coupons()
        {
            await CoffeeHalfPriceAsync();
            await BurgerMealAsync();
            await SetLineAsync(10, 3, 2);
            await _svc.ApplyAsync(10);
            _db.Add(new Coupon { Id = 1, Code = "SAVE2", Name = "Save two", Type = CouponType.Fixed, Value = 2m, Redemptions = { new CouponRedemption { TransactionId = 10, DiscountAmount = 2m } } });
            await _db.SaveChangesAsync();

            var r = await _svc.GetAppliedAsync(10);
            r.Success.Should().BeTrue();
            r.Data!.IsOpen.Should().BeTrue();
            r.Data.HappyHour!.RuleName.Should().Be("Coffee hour");
            r.Data.HappyHour.Amount.Should().Be(5m);
            r.Data.Combos.Should().ContainSingle(c => c.ComboName == "Burger meal" && c.Quantity == 1 && c.Amount == 3m);
            r.Data.Coupons.Should().ContainSingle(c => c.Code == "SAVE2" && c.Amount == 2m);
            r.Data.TotalDiscount.Should().Be(10m);

            (await _svc.GetAppliedAsync(20)).Data!.IsOpen.Should().BeFalse();
            (await _svc.GetAppliedAsync(999)).Success.Should().BeFalse();
        }
    }
}
