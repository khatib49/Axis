using Application.DTOs;
using Domain.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.AccountingPlus
{
    public class SupplierBillTests : IDisposable
    {
        private static readonly DateTime Day = new(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc);

        [Fact]
        public async Task Opening_a_bill_posts_expense_against_payable_through_the_journal()
        {
            using var h = new AccountingPlusTestHost();
            var r = await h.Bills.CreateAsync(new SupplierBillCreateDto(1, "INV-1", Day, Day.AddDays(30), 100, 11, AccountingPlusTestHost.Expense, null, null, "cheese", "Open"));
            r.Success.Should().BeTrue(r.Error);
            r.Data!.Status.Should().Be("Open");
            r.Data.Total.Should().Be(111);
            r.Data.PayableGlAccountId.Should().Be(AccountingPlusTestHost.Payable, "2000 Accounts Payable is the default");

            var je = await h.Db.Set<JournalEntry>().Include(j => j.Lines).SingleAsync(j => j.Id == r.Data.JournalEntryId);
            je.IsPosted.Should().BeTrue();
            je.ReferenceType.Should().Be("SupplierBill");
            // no VAT-input account → tax rolled into expense
            je.Lines.Single(l => l.AccountId == AccountingPlusTestHost.Expense).DebitAmount.Should().Be(111);
            je.Lines.Single(l => l.AccountId == AccountingPlusTestHost.Payable).CreditAmount.Should().Be(111);
        }

        [Fact]
        public async Task Tax_goes_to_vat_input_account_when_a_purchase_rate_and_account_exist()
        {
            using var h = new AccountingPlusTestHost(withVatInputAccount: true);
            (await h.Tax.CreateRateAsync(new TaxRateUpsertDto("VAT 11%", 11, true, true, "Both"))).Success.Should().BeTrue();
            var r = await h.Bills.CreateAsync(new SupplierBillCreateDto(1, "INV-2", Day, Day.AddDays(30), 100, 11, AccountingPlusTestHost.Expense, null, null, null, "Open"));
            r.Success.Should().BeTrue(r.Error);
            var je = await h.Db.Set<JournalEntry>().Include(j => j.Lines).SingleAsync(j => j.Id == r.Data!.JournalEntryId);
            je.Lines.Single(l => l.AccountId == AccountingPlusTestHost.Expense).DebitAmount.Should().Be(100);
            je.Lines.Single(l => l.AccountId == h.VatInput!.Value).DebitAmount.Should().Be(11);
            je.Lines.Single(l => l.AccountId == AccountingPlusTestHost.Payable).CreditAmount.Should().Be(111);
        }

        [Fact]
        public async Task Payments_move_status_open_to_partially_paid_to_paid_and_cannot_overpay()
        {
            using var h = new AccountingPlusTestHost();
            var bill = (await h.Bills.CreateAsync(new SupplierBillCreateDto(1, "INV-3", Day, Day.AddDays(30), 200, 0, AccountingPlusTestHost.Expense, null, null, null, "Open"))).Data!;

            var p1 = await h.Bills.AddPaymentAsync(bill.Id, new SupplierBillPaymentCreateDto(Day.AddDays(5), 80, "Cash", "R1", AccountingPlusTestHost.Cash));
            p1.Success.Should().BeTrue(p1.Error);
            p1.Data!.Status.Should().Be("PartiallyPaid");
            p1.Data.PaidAmount.Should().Be(80);
            p1.Data.Outstanding.Should().Be(120);

            var over = await h.Bills.AddPaymentAsync(bill.Id, new SupplierBillPaymentCreateDto(Day.AddDays(6), 120.01m, "Cash", null, AccountingPlusTestHost.Cash));
            over.Success.Should().BeFalse();

            var p2 = await h.Bills.AddPaymentAsync(bill.Id, new SupplierBillPaymentCreateDto(Day.AddDays(6), 120, "Bank", "R2", AccountingPlusTestHost.BankGl));
            p2.Success.Should().BeTrue(p2.Error);
            p2.Data!.Status.Should().Be("Paid");
            p2.Data.Outstanding.Should().Be(0);
            p2.Data.Payments.Should().HaveCount(2);

            var again = await h.Bills.AddPaymentAsync(bill.Id, new SupplierBillPaymentCreateDto(Day.AddDays(7), 1, "Cash", null, AccountingPlusTestHost.Cash));
            again.Success.Should().BeFalse("paid bills take no more payments");

            // every payment has its own posted journal: DR payable / CR cash
            var payJes = await h.Db.Set<JournalEntry>().Include(j => j.Lines).Where(j => j.ReferenceType == "SupplierBillPayment").ToListAsync();
            payJes.Should().HaveCount(2);
            payJes.All(j => j.IsPosted).Should().BeTrue();
            payJes.Sum(j => j.Lines.Single(l => l.AccountId == AccountingPlusTestHost.Payable).DebitAmount).Should().Be(200);
        }

        [Fact]
        public async Task Draft_bills_do_not_post_until_opened_and_voiding_reverses_bill_and_payments()
        {
            using var h = new AccountingPlusTestHost();
            var draft = await h.Bills.CreateAsync(new SupplierBillCreateDto(1, "INV-4", Day, Day.AddDays(30), 50, 0, AccountingPlusTestHost.Expense, null, null, null, "Draft"));
            draft.Data!.Status.Should().Be("Draft");
            draft.Data.JournalEntryId.Should().BeNull();
            (await h.Db.Set<JournalEntry>().CountAsync()).Should().Be(0);

            var opened = await h.Bills.OpenAsync(draft.Data.Id);
            opened.Data!.Status.Should().Be("Open");
            opened.Data.JournalEntryId.Should().NotBeNull();

            await h.Bills.AddPaymentAsync(draft.Data.Id, new SupplierBillPaymentCreateDto(Day.AddDays(1), 20, "Cash", null, AccountingPlusTestHost.Cash));
            var voided = await h.Bills.VoidAsync(draft.Data.Id, "wrong supplier");
            voided.Success.Should().BeTrue(voided.Error);
            voided.Data!.Status.Should().Be("Void");

            var originals = await h.Db.Set<JournalEntry>().Where(j => j.ReferenceType == "SupplierBill" || j.ReferenceType == "SupplierBillPayment").ToListAsync();
            originals.Should().HaveCount(2);
            originals.All(j => j.IsVoided).Should().BeTrue();
            // reversing entries leave the payable balance at zero
            var payable = await h.Db.Set<Account>().SingleAsync(a => a.Id == AccountingPlusTestHost.Payable);
            payable.CurrentBalance.Should().Be(0);
        }

        [Fact]
        public async Task Duplicate_bill_numbers_per_supplier_and_unknown_suppliers_are_rejected()
        {
            using var h = new AccountingPlusTestHost();
            (await h.Bills.CreateAsync(new SupplierBillCreateDto(1, "DUP", Day, Day, 10, 0, AccountingPlusTestHost.Expense, null, null, null))).Success.Should().BeTrue();
            (await h.Bills.CreateAsync(new SupplierBillCreateDto(1, "DUP", Day, Day, 10, 0, AccountingPlusTestHost.Expense, null, null, null))).Success.Should().BeFalse();
            (await h.Bills.CreateAsync(new SupplierBillCreateDto(2, "DUP", Day, Day, 10, 0, AccountingPlusTestHost.Expense, null, null, null))).Success.Should().BeTrue("same number, other supplier");
            (await h.Bills.CreateAsync(new SupplierBillCreateDto(99, "X", Day, Day, 10, 0, AccountingPlusTestHost.Expense, null, null, null))).Success.Should().BeFalse();
        }

        public void Dispose() { }
    }
}
