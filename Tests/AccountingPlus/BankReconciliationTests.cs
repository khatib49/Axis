using Application.DTOs;
using Application.Services.AccountingPlus;
using FluentAssertions;
using Xunit;

namespace Tests.AccountingPlus
{
    public class BankReconciliationTests : IDisposable
    {
        private readonly AccountingPlusTestHost _h = new();
        private static readonly DateTime D = new(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

        public void Dispose() => _h.Dispose();

        [Fact]
        public void Csv_parser_accepts_both_date_formats_and_a_header_in_any_column_order()
        {
            var csv = "amount,reference,date,description\n" +
                      "150.00,REF1,12/08/2026,Deposit\n" +
                      "\"-1,200.50\",REF2,2026-08-13,\"Rent, August\"\n" +
                      "oops,REF3,2026-08-14,bad\n";
            var (lines, errors, skipped) = BankReconciliationService.ParseCsv(csv);
            lines.Should().HaveCount(2);
            lines[0].Date.Should().Be(new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc));
            lines[0].Amount.Should().Be(150m);
            lines[1].Amount.Should().Be(-1200.50m);
            lines[1].Description.Should().Be("Rent, August");
            skipped.Should().Be(1);
            errors.Should().ContainSingle(e => e.Contains("amount"));
        }

        [Fact]
        public async Task Suggestions_only_return_posted_lines_on_the_bank_account_within_three_days_and_same_absolute_amount()
        {
            var bank = (await _h.Bank.CreateBankAccountAsync(new BankAccountUpsertDto("Main", "BLOM", "****1234", "USD", AccountingPlusTestHost.BankGl))).Data!;
            var exact = await _h.PostBankMovementAsync(D, 500, "same day");
            var edge = await _h.PostBankMovementAsync(D.AddDays(3), -500, "3 days later, opposite sign");
            var tooFar = await _h.PostBankMovementAsync(D.AddDays(4), 500, "4 days later");
            var wrongAmount = await _h.PostBankMovementAsync(D, 499, "wrong amount");
            // unposted entry on the bank account must not appear
            await _h.Journal.CreateJournalEntryAsync(new JournalEntryCreateDto(D, "unposted", "Adjustment", null,
                new List<JournalEntryLineCreateDto> { new(AccountingPlusTestHost.BankGl, 500, 0, null), new(AccountingPlusTestHost.Cash, 0, 500, null) }), 1);

            var st = (await _h.Bank.CreateStatementAsync(new BankStatementCreateDto(bank.Id, D.AddDays(-9), D.AddDays(20), 0, 500,
                new List<BankStatementLineCreateDto> { new(D, "Deposit", "R1", 500), new(D.AddDays(1), "Other", "R2", 500) }))).Data!;
            var detail = (await _h.Bank.GetStatementAsync(st.Id, null)).Data!;
            var line1 = detail.Lines[0];
            var line2 = detail.Lines[1];

            var s = (await _h.Bank.GetSuggestionsAsync(st.Id, line1.Id)).Data!;
            s.Select(x => x.JournalEntryLineId).Should().BeEquivalentTo(new[] { exact.Id, edge.Id });
            s[0].JournalEntryLineId.Should().Be(exact.Id, "same sign and same day ranks first");
            s.Should().NotContain(x => x.JournalEntryLineId == tooFar.Id || x.JournalEntryLineId == wrongAmount.Id);

            // once a ledger line is matched elsewhere it drops out of the suggestions
            (await _h.Bank.MatchLineAsync(st.Id, line2.Id, exact.Id)).Success.Should().BeTrue();
            (await _h.Bank.GetSuggestionsAsync(st.Id, line1.Id)).Data!.Select(x => x.JournalEntryLineId).Should().Equal(edge.Id);
            (await _h.Bank.MatchLineAsync(st.Id, line1.Id, exact.Id)).Success.Should().BeFalse("already taken");
            (await _h.Bank.MatchLineAsync(st.Id, line1.Id, wrongAmount.Id)).Success.Should().BeTrue("manual match may ignore the amount heuristic");
        }

        [Fact]
        public async Task Reconcile_requires_no_unmatched_lines_and_zero_difference()
        {
            var bank = (await _h.Bank.CreateBankAccountAsync(new BankAccountUpsertDto("Main", null, null, null, AccountingPlusTestHost.BankGl))).Data!;
            var dep = await _h.PostBankMovementAsync(D, 1000);
            var wd = await _h.PostBankMovementAsync(D.AddDays(2), -250);

            var st = (await _h.Bank.CreateStatementAsync(new BankStatementCreateDto(bank.Id, D.AddDays(-9), D.AddDays(20), 0, 750,
                new List<BankStatementLineCreateDto> { new(D, "Deposit", null, 1000), new(D.AddDays(2), "ATM", null, -250), new(D.AddDays(3), "Bank fee", null, -1) }))).Data!;
            var lines = (await _h.Bank.GetStatementAsync(st.Id, null)).Data!.Lines;

            var summary = (await _h.Bank.GetSummaryAsync(st.Id)).Data!;
            summary.LedgerBalanceAsOfPeriodTo.Should().Be(750);
            summary.Difference.Should().Be(0);
            summary.UnmatchedCount.Should().Be(3);
            summary.CanReconcile.Should().BeFalse();
            (await _h.Bank.ReconcileAsync(st.Id)).Success.Should().BeFalse();

            await _h.Bank.MatchLineAsync(st.Id, lines[0].Id, dep.Id);
            await _h.Bank.MatchLineAsync(st.Id, lines[1].Id, wd.Id);
            await _h.Bank.ExcludeLineAsync(st.Id, lines[2].Id, "fee not booked yet");
            summary = (await _h.Bank.GetSummaryAsync(st.Id)).Data!;
            summary.UnmatchedCount.Should().Be(0);
            summary.ExcludedCount.Should().Be(1);
            summary.CanReconcile.Should().BeTrue();

            var done = await _h.Bank.ReconcileAsync(st.Id);
            done.Success.Should().BeTrue(done.Error);
            done.Data!.Status.Should().Be("Reconciled");
            (await _h.Bank.UnmatchLineAsync(st.Id, lines[0].Id)).Success.Should().BeFalse("reconciled statements are frozen");
        }

        [Fact]
        public async Task Difference_blocks_reconcile_when_closing_balance_disagrees_with_the_ledger()
        {
            var bank = (await _h.Bank.CreateBankAccountAsync(new BankAccountUpsertDto("Main", null, null, null, AccountingPlusTestHost.BankGl))).Data!;
            var dep = await _h.PostBankMovementAsync(D, 1000);
            var st = (await _h.Bank.CreateStatementAsync(new BankStatementCreateDto(bank.Id, D.AddDays(-9), D.AddDays(20), 0, 990,
                new List<BankStatementLineCreateDto> { new(D, "Deposit", null, 1000) }))).Data!;
            var line = (await _h.Bank.GetStatementAsync(st.Id, null)).Data!.Lines[0];
            await _h.Bank.MatchLineAsync(st.Id, line.Id, dep.Id);
            var r = await _h.Bank.ReconcileAsync(st.Id);
            r.Success.Should().BeFalse();
            r.Data!.Difference.Should().Be(-10);
        }
    }
}
