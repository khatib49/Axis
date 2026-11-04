using Application.DTOs;
using Application.Services.Promotions;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.Promotions
{
    /// <summary>
    /// Phase 3: gift-card liability and promotion (contra-revenue) accounting
    /// posted through the real JournalService / AccountService on InMemory.
    /// </summary>
    public class PromotionAccountingTests : IDisposable
    {
        private readonly ApplicationDbContext _db = PromoDb.New();
        private readonly PromotionAccountingService _acct;

        public PromotionAccountingTests()
        {
            PromoDb.SeedCatalogAndInvoices(_db); // open invoice #10 (total 20), paid #20
            PromoDb.SeedChartOfAccounts(_db);    // 1000, 4100, 4110 + account types; 2150 / 4950 missing on purpose
            _acct = PromoAccounting.New(_db);
        }

        public void Dispose() => _db.Dispose();

        private static GiftCard Card(decimal balance) => new()
        {
            Id = 1, Code = "ABCDEFGHJKLMNPQR", InitialAmount = balance, Balance = balance, Status = GiftCardStatus.Active,
        };

        private Task<Account?> AccountAsync(string number)
            => _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.AccountNumber == number);

        /// <summary>Posted, non-voided journal entries for a reference, with their lines and account numbers.</summary>
        private async Task<List<JournalEntry>> EntriesAsync(string referenceType, int referenceId)
            => await _db.Set<JournalEntry>().AsNoTracking().Include(e => e.Lines).ThenInclude(l => l.Account)
                .Where(e => e.ReferenceType == referenceType && e.ReferenceId == referenceId && !e.IsVoided)
                .OrderBy(e => e.Id).ToListAsync();

        private static void AssertEntry(JournalEntry je, string debitNo, string creditNo, decimal amount)
        {
            je.IsPosted.Should().BeTrue("promotion postings are auto-posted");
            je.Lines.Should().HaveCount(2);
            je.Lines.Sum(l => l.DebitAmount).Should().Be(je.Lines.Sum(l => l.CreditAmount), "entries are balanced");
            var dr = je.Lines.Single(l => l.DebitAmount > 0);
            var cr = je.Lines.Single(l => l.CreditAmount > 0);
            dr.Account.AccountNumber.Should().Be(debitNo);
            dr.DebitAmount.Should().Be(amount);
            cr.Account.AccountNumber.Should().Be(creditNo);
            cr.CreditAmount.Should().Be(amount);
        }

        private async Task<decimal> LedgerBalanceAsync(string number, bool creditNormal)
        {
            var acc = await AccountAsync(number);
            if (acc is null) return 0m;
            var lines = await _db.Set<JournalEntryLine>().AsNoTracking()
                .Where(l => l.AccountId == acc.Id && l.JournalEntry.IsPosted && !l.JournalEntry.IsVoided).ToListAsync();
            var dr = lines.Sum(l => l.DebitAmount); var cr = lines.Sum(l => l.CreditAmount);
            return creditNormal ? cr - dr : dr - cr;
        }

        // ── accounts ─────────────────────────────────────────────────────
        [Fact]
        public async Task Accounts_2150_and_4950_are_created_once_with_the_right_types_and_flags()
        {
            (await AccountAsync("2150")).Should().BeNull();
            (await AccountAsync("4950")).Should().BeNull();

            var first = await _acct.EnsureAccountsAsync();
            first.Success.Should().BeTrue(first.Error);
            first.Data!.CreatedLiability.Should().BeTrue();
            first.Data.CreatedDiscount.Should().BeTrue();
            first.Data.CashAccountNumber.Should().Be("1000");
            first.Data.SalesAccountNumber.Should().Be("4100", "the general F&B sales account the transaction journal credits");

            var liability = (await AccountAsync("2150"))!;
            liability.AccountName.Should().Be("Gift Card Liability");
            liability.AccountTypeId.Should().Be(2, "Liability");
            liability.AllowManualEntry.Should().BeFalse();
            liability.IsSystemAccount.Should().BeTrue();

            var discount = (await AccountAsync("4950"))!;
            discount.AccountName.Should().Be("Promotional Discounts");
            discount.AccountTypeId.Should().Be(4, "Revenue type (contra-revenue)");
            discount.Description.Should().Contain("Contra-revenue");

            var second = await _acct.EnsureAccountsAsync();
            second.Success.Should().BeTrue();
            second.Data!.CreatedLiability.Should().BeFalse();
            second.Data.CreatedDiscount.Should().BeFalse();
            (await _db.Set<Account>().CountAsync(a => a.AccountNumber == "2150" || a.AccountNumber == "4950")).Should().Be(2);
        }

        // ── gift cards ───────────────────────────────────────────────────
        [Fact]
        public async Task Issue_load_redeem_refund_post_balanced_entries_on_the_right_accounts()
        {
            var cards = PromoAccounting.GiftCards(_db, _acct);

            var issued = await cards.IssueAsync(new GiftCardIssueDto(50m));
            issued.Success.Should().BeTrue(issued.Message);
            issued.Message.Should().Be("Gift card issued", "no accounting warning when the period is open");
            var cardId = issued.Data!.Id;
            var loadTx = await _db.Set<GiftCardTransaction>().AsNoTracking().SingleAsync(t => t.GiftCardId == cardId && t.Type == "Load");
            var loadEntries = await EntriesAsync("GiftCardLoad", loadTx.Id);
            loadEntries.Should().HaveCount(1);
            AssertEntry(loadEntries[0], "1000", "2150", 50m);

            var loaded = await cards.LoadAsync(cardId, new GiftCardLoadDto(20m, null));
            loaded.Success.Should().BeTrue(loaded.Message);
            var loadTx2 = await _db.Set<GiftCardTransaction>().AsNoTracking().Where(t => t.GiftCardId == cardId && t.Type == "Load").OrderBy(t => t.Id).LastAsync();
            AssertEntry((await EntriesAsync("GiftCardLoad", loadTx2.Id)).Single(), "1000", "2150", 20m);

            var redeemed = await cards.RedeemAsync(new GiftCardRedeemDto(issued.Data.Code, 10, 15m));
            redeemed.Success.Should().BeTrue(redeemed.Message);
            var redeemTx = await _db.Set<GiftCardTransaction>().AsNoTracking().SingleAsync(t => t.GiftCardId == cardId && t.Type == "Redeem");
            var redeemEntry = (await EntriesAsync("GiftCardRedeem", redeemTx.Id)).Single();
            AssertEntry(redeemEntry, "2150", "4100", 15m);
            redeemEntry.Description.Should().Contain("invoice #10");

            var refunded = await cards.RefundAsync(cardId, new GiftCardRefundDto(5m, 10, "void line"));
            refunded.Success.Should().BeTrue(refunded.Message);
            var refundTx = await _db.Set<GiftCardTransaction>().AsNoTracking().SingleAsync(t => t.GiftCardId == cardId && t.Type == "Refund");
            AssertEntry((await EntriesAsync("GiftCardRefund", refundTx.Id)).Single(), "4100", "2150", 5m);

            // Ledger 2150: +50 +20 −15 +5 = 60 = card balance; cash 1000: +50 +20 = 70 (a refund to card moves no cash); 4100: 15 − 5 = 10
            (await LedgerBalanceAsync("2150", creditNormal: true)).Should().Be(60m);
            (await LedgerBalanceAsync("1000", creditNormal: false)).Should().Be(70m);
            (await LedgerBalanceAsync("4100", creditNormal: true)).Should().Be(10m);
            (await _db.Set<GiftCard>().AsNoTracking().SingleAsync(c => c.Id == cardId)).Balance.Should().Be(60m);

            var report = await _acct.GetGiftCardLiabilityReportAsync();
            report.Success.Should().BeTrue();
            report.Data!.ActiveCards.Should().Be(1);
            report.Data.ActiveCardBalance.Should().Be(60m);
            report.Data.LedgerBalance.Should().Be(60m);
            report.Data.Difference.Should().Be(0m);
            report.Data.PendingPostings.Should().Be(0);

            (await _db.Set<PromotionAccountingPosting>().CountAsync()).Should().Be(4);
            (await _db.Set<PromotionAccountingPosting>().AllAsync(p => p.Status == "Posted" && p.JournalEntryId != null)).Should().BeTrue();
        }

        // ── promotion discounts ──────────────────────────────────────────
        [Fact]
        public async Task Coupon_redeem_posts_contra_revenue_for_the_discount()
        {
            _db.Add(new Coupon { Id = 1, Code = "FOOD50", Name = "Half food", Type = CouponType.Percent, Value = 50m, CategoryIdsCsv = "2", IsActive = true });
            await _db.SaveChangesAsync();
            var coupons = PromoAccounting.Coupons(_db, _acct);

            var r = await coupons.RedeemAsync(new CouponRedeemDto("FOOD50", 10)); // burger 10 → 5 off
            r.Success.Should().BeTrue(r.Message);
            r.Data!.DiscountAmount.Should().Be(5m);
            r.Message.Should().Be("Coupon applied");

            var je = (await EntriesAsync("PromotionCoupon", r.Data.RedemptionId)).Single();
            AssertEntry(je, "4950", "4100", 5m);
            je.Description.Should().Contain("invoice #10");

            var posting = await _db.Set<PromotionAccountingPosting>().SingleAsync();
            posting.ReferenceType.Should().Be("PromotionCoupon");
            posting.ReferenceId.Should().Be(r.Data.RedemptionId);
            posting.Sequence.Should().Be(1);
            posting.Amount.Should().Be(-5m);
            posting.InvoiceId.Should().Be(10);
            posting.Status.Should().Be("Posted");
        }

        [Fact]
        public async Task Auto_apply_deltas_post_contra_revenue_and_a_refund_delta_reverses_it()
        {
            // Always-on 50% off coffee (category 1): invoice #10 has 2 lattes (10) → 5 off.
            _db.Add(new HappyHourRule { Id = 1, Name = "Coffee hour", DaysOfWeekCsv = "", StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(0, 0), DiscountPercent = 50m, CategoryIdsCsv = "1", IsActive = true });
            // Burger meal: burger (10) + 2 fries (8) = 18 for 15 → saving 3 per set.
            var combo = new Combo { Id = 1, Name = "Burger meal", Price = 15m, IsActive = true };
            combo.Items.Add(new ComboItem { ItemId = 2, Quantity = 1 });
            combo.Items.Add(new ComboItem { ItemId = 3, Quantity = 2 });
            _db.Add(combo);
            var trx = await _db.Transactions.SingleAsync(t => t.Id == 10);
            trx.TransactionItems.Add(new TransactionItem { TransactionRecordId = 10, ItemId = 3, Quantity = 2 });
            trx.TotalPrice += 8m; // 28
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();

            var auto = PromoAccounting.AutoApply(_db, _acct);
            var first = await auto.ApplyAsync(10);
            first.Success.Should().BeTrue(first.Message);
            first.Data!.HappyHour!.Delta.Should().Be(-5m);
            first.Data.Combos.Single().Delta.Should().Be(-3m);
            first.Data.Messages.Should().NotContain(m => m.Contains("Accounting pending"));

            var hhApp = await _db.Set<HappyHourApplication>().AsNoTracking().SingleAsync(a => a.TransactionId == 10);
            var comboApp = await _db.Set<ComboApplication>().AsNoTracking().SingleAsync(a => a.TransactionId == 10);
            AssertEntry((await EntriesAsync("PromotionHappyHour", hhApp.Id)).Single(), "4950", "4100", 5m);
            AssertEntry((await EntriesAsync("PromotionCombo", comboApp.Id)).Single(), "4950", "4100", 3m);

            // Second pass changes nothing → no new postings.
            var again = await auto.ApplyAsync(10);
            again.Data!.TotalBefore.Should().Be(again.Data.TotalAfter);
            (await _db.Set<PromotionAccountingPosting>().CountAsync()).Should().Be(2);

            // Fries removed → combo no longer forms a set → 3 refunded → DR 4100 / CR 4950.
            _db.ChangeTracker.Clear();
            var fries = await _db.Set<TransactionItem>().SingleAsync(ti => ti.TransactionRecordId == 10 && ti.ItemId == 3);
            _db.Remove(fries);
            var t2 = await _db.Transactions.SingleAsync(t => t.Id == 10);
            t2.TotalPrice -= 8m;
            await _db.SaveChangesAsync();
            _db.ChangeTracker.Clear();

            var third = await PromoAccounting.AutoApply(_db, _acct).ApplyAsync(10);
            third.Data!.Combos.Single().Delta.Should().Be(3m);
            var comboApp2 = await _db.Set<ComboApplication>().AsNoTracking().SingleAsync(a => a.TransactionId == 10);
            comboApp2.Version.Should().Be(2);
            var comboEntries = await EntriesAsync("PromotionCombo", comboApp2.Id);
            comboEntries.Should().HaveCount(2);
            AssertEntry(comboEntries[1], "4100", "4950", 3m);
            var postings = await _db.Set<PromotionAccountingPosting>().AsNoTracking().Where(p => p.ReferenceType == "PromotionCombo").OrderBy(p => p.Sequence).ToListAsync();
            postings.Select(p => (p.Sequence, p.Amount)).Should().Equal((1, -3m), (2, 3m));

            // Net contra-revenue: 5 (happy hour) + 3 − 3 (combo) = 5 debit on 4950.
            (await LedgerBalanceAsync("4950", creditNormal: false)).Should().Be(5m);

            var report = await _acct.GetDiscountsReportAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));
            var hh = report.Data!.Kinds.Single(k => k.Kind == "HappyHour");
            hh.Applied.Should().Be(1); hh.NetDiscount.Should().Be(5m); hh.PostedAmount.Should().Be(5m);
            var cb = report.Data.Kinds.Single(k => k.Kind == "Combo");
            cb.Applied.Should().Be(1); cb.Refunded.Should().Be(1); cb.DiscountGiven.Should().Be(3m); cb.DiscountRefunded.Should().Be(3m); cb.NetDiscount.Should().Be(0m);
            report.Data.TotalNetDiscount.Should().Be(5m);
        }

        // ── idempotency ──────────────────────────────────────────────────
        [Fact]
        public async Task Posting_the_same_reference_twice_books_one_entry_and_reports_already_posted()
        {
            var a = await _acct.OnGiftCardIssuedOrLoadedAsync(1, 77, 50m);
            a.Success.Should().BeTrue(a.Error);
            a.Data!.AlreadyPosted.Should().BeFalse();
            a.Data.EntryNumber.Should().NotBeNullOrEmpty();

            var b = await _acct.OnGiftCardIssuedOrLoadedAsync(1, 77, 50m);
            b.Success.Should().BeTrue();
            b.Data!.AlreadyPosted.Should().BeTrue();
            b.Data.JournalEntryId.Should().Be(a.Data.JournalEntryId);

            (await EntriesAsync("GiftCardLoad", 77)).Should().HaveCount(1);
            (await _db.Set<PromotionAccountingPosting>().CountAsync()).Should().Be(1);

            // Same application row, next version → a new posting.
            (await _acct.OnPromotionDiscountAsync("Combo", 5, 1, 10, -3m)).Success.Should().BeTrue();
            (await _acct.OnPromotionDiscountAsync("Combo", 5, 1, 10, -3m)).Data!.AlreadyPosted.Should().BeTrue();
            (await _acct.OnPromotionDiscountAsync("Combo", 5, 2, 10, 3m)).Data!.AlreadyPosted.Should().BeFalse();
            (await EntriesAsync("PromotionCombo", 5)).Should().HaveCount(2);

            (await _acct.OnPromotionDiscountAsync("Combo", 6, 1, 10, 0m)).Success.Should().BeFalse("zero delta posts nothing");
            (await _acct.OnPromotionDiscountAsync("Bogus", 6, 1, 10, -1m)).Success.Should().BeFalse();
        }

        // ── period lock ──────────────────────────────────────────────────
        [Fact]
        public async Task Locked_period_keeps_the_business_action_leaves_the_posting_pending_and_repost_books_it_after_unlock()
        {
            var periods = PromoAccounting.Periods(_db);
            var now = DateTime.UtcNow;
            (await periods.LockAsync(new PeriodLockRequestDto(now.Year, now.Month, "month end"))).Success.Should().BeTrue();

            var cards = PromoAccounting.GiftCards(_db, _acct);
            var issued = await cards.IssueAsync(new GiftCardIssueDto(50m));
            issued.Success.Should().BeTrue("the gift card is issued even though the ledger is locked");
            issued.Message.Should().StartWith("Gift card issued").And.Contain("accounting pending").And.Contain("locked");
            (await _db.Set<GiftCard>().CountAsync()).Should().Be(1);

            var redeemed = await cards.RedeemAsync(new GiftCardRedeemDto(issued.Data!.Code, 10, 15m));
            redeemed.Success.Should().BeTrue();
            redeemed.Message.Should().Contain("accounting pending");
            (await _db.Transactions.AsNoTracking().SingleAsync(t => t.Id == 10)).TotalPrice.Should().Be(5m);

            var pending = await _acct.GetPendingAsync();
            pending.Should().HaveCount(2);
            pending.Should().OnlyContain(p => p.Status == "Pending" && p.JournalEntryId == null && p.Error!.Contains("locked"));
            (await _db.Set<JournalEntry>().CountAsync()).Should().Be(0, "nothing reaches the journal while locked");

            var liability = await _acct.GetGiftCardLiabilityReportAsync();
            liability.Data!.ActiveCardBalance.Should().Be(35m);
            liability.Data.LedgerBalance.Should().Be(0m);
            liability.Data.PendingPostings.Should().Be(2);
            liability.Data.PendingAmount.Should().Be(35m);

            // Still locked → repost books nothing.
            var blocked = await _acct.RepostPendingAsync();
            blocked.Data!.Attempted.Should().Be(2);
            blocked.Data.Posted.Should().Be(0);
            blocked.Data.StillPending.Should().Be(2);

            (await periods.UnlockAsync(now.Year, now.Month)).Success.Should().BeTrue();
            var repost = await _acct.RepostPendingAsync();
            repost.Success.Should().BeTrue();
            repost.Data!.Posted.Should().Be(2);
            repost.Data.StillPending.Should().Be(0);
            (await _acct.GetPendingAsync()).Should().BeEmpty();

            var loadTx = await _db.Set<GiftCardTransaction>().AsNoTracking().SingleAsync(t => t.Type == "Load");
            AssertEntry((await EntriesAsync("GiftCardLoad", loadTx.Id)).Single(), "1000", "2150", 50m);
            var redeemTx = await _db.Set<GiftCardTransaction>().AsNoTracking().SingleAsync(t => t.Type == "Redeem");
            AssertEntry((await EntriesAsync("GiftCardRedeem", redeemTx.Id)).Single(), "2150", "4100", 15m);

            var after = await _acct.GetGiftCardLiabilityReportAsync();
            after.Data!.LedgerBalance.Should().Be(35m);
            after.Data.Difference.Should().Be(0m);
            after.Data.PendingPostings.Should().Be(0);

            (await _acct.RepostPendingAsync()).Data!.Attempted.Should().Be(0);
        }

        [Fact]
        public async Task Missing_chart_of_accounts_defers_the_posting_instead_of_failing_the_till()
        {
            using var bare = PromoDb.New();
            PromoDb.SeedCatalogAndInvoices(bare); // no account types / accounts at all
            var acct = PromoAccounting.New(bare);
            var cards = PromoAccounting.GiftCards(bare, acct);

            var issued = await cards.IssueAsync(new GiftCardIssueDto(25m));
            issued.Success.Should().BeTrue();
            issued.Message.Should().Contain("accounting pending");
            var pending = await acct.GetPendingAsync();
            pending.Should().ContainSingle().Which.Error.Should().Contain("Liability");
        }

        // ── reports / backfill ───────────────────────────────────────────
        [Fact]
        public async Task Backfill_books_historic_movements_once_and_postings_list_shows_accounts()
        {
            // A card issued and redeemed before accounting existed (ledger rows only).
            var card = Card(40m);
            card.Transactions.Add(new GiftCardTransaction { Type = "Load", Amount = 50m, BalanceAfter = 50m, On = DateTime.UtcNow.AddMinutes(-10) });
            card.Transactions.Add(new GiftCardTransaction { Type = "Redeem", Amount = 10m, TransactionId = 20, BalanceAfter = 40m, On = DateTime.UtcNow.AddMinutes(-5) });
            _db.Add(card);
            _db.Add(new Coupon { Id = 1, Code = "FLAT5", Name = "Flat 5", Type = CouponType.Fixed, Value = 5m, IsActive = true, UsedCount = 1 });
            _db.Add(new CouponRedemption { CouponId = 1, TransactionId = 20, DiscountAmount = 5m, RedeemedOn = DateTime.UtcNow.AddMinutes(-3), RedeemedBy = "seed" });
            await _db.SaveChangesAsync();

            var run = await _acct.BackfillAsync();
            run.Success.Should().BeTrue();
            run.Data!.Attempted.Should().Be(3);
            run.Data.Posted.Should().Be(3);

            (await LedgerBalanceAsync("2150", creditNormal: true)).Should().Be(40m);
            (await LedgerBalanceAsync("4950", creditNormal: false)).Should().Be(5m);
            (await _acct.GetGiftCardLiabilityReportAsync()).Data!.Difference.Should().Be(0m);

            var again = await _acct.BackfillAsync();
            again.Data!.Attempted.Should().Be(0, "every movement already has a posting row");
            (await _db.Set<JournalEntry>().CountAsync()).Should().Be(3);

            var postings = await _acct.GetPostingsAsync(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), null);
            postings.Should().HaveCount(3);
            postings.Should().OnlyContain(p => p.Status == "Posted" && p.EntryNumber != null);
            var redeem = postings.Single(p => p.ReferenceType == "GiftCardRedeem");
            redeem.DebitAccount.Should().Be("2150");
            redeem.CreditAccount.Should().Be("4100");
            redeem.Kind.Should().Be("GiftCardRedeem");
            postings.Single(p => p.ReferenceType == "PromotionCoupon").Kind.Should().Be("Coupon");
            (await _acct.GetPostingsAsync(null, null, "Pending")).Should().BeEmpty();
        }
    }
}
