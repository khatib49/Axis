using Application.Mapping;
using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.Accounting
{
    /// <summary>
    /// Report pages send plain dates ("2026-10-06" = midnight). The chosen
    /// day must be included in full: an entry posted at 18:00 on the as-of /
    /// "to" day belongs in the report, one on the next day does not.
    /// </summary>
    public class ReportDateRangeTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountService _svc;
        private static readonly DateTime Day = new(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);

        public ReportDateRangeTests()
        {
            _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            _db.AccountTypes.AddRange(
                new AccountType { Id = 1, TypeName = "Asset", NormalBalance = "Debit" },
                new AccountType { Id = 4, TypeName = "Revenue", NormalBalance = "Credit" });
            _db.Accounts.AddRange(
                new Account { Id = 10, AccountNumber = "1000", AccountName = "Cash on Hand", AccountTypeId = 1 },
                new Account { Id = 40, AccountNumber = "4100", AccountName = "F&B Revenue", AccountTypeId = 4 });

            Sale(1, Day.AddDays(-1).AddHours(12), 100m);  // the day before
            Sale(2, Day.AddHours(18), 50m);               // later on the chosen day
            Sale(3, Day.AddDays(1).AddHours(9), 999m);    // the next day
            _db.JournalEntries.Add(new JournalEntry
            {
                Id = 4, EntryNumber = "JE-4", EntryDate = Day.AddHours(10), Description = "voided", ReferenceType = "Adjustment",
                TotalAmount = 7m, IsPosted = true, IsVoided = true,
                Lines = { new JournalEntryLine { AccountId = 10, DebitAmount = 7m, LineNumber = 1 }, new JournalEntryLine { AccountId = 40, CreditAmount = 7m, LineNumber = 2 } },
            });
            _db.SaveChanges();

            _svc = new AccountService(
                new BaseRepository<Account>(_db), new BaseRepository<AccountType>(_db),
                new BaseRepository<JournalEntryLine>(_db), new BaseRepository<JournalEntry>(_db),
                new UnitOfWork(_db), new AccountingMapper(), NullLogger<AccountService>.Instance);
        }

        private void Sale(int id, DateTime at, decimal amount) =>
            _db.JournalEntries.Add(new JournalEntry
            {
                Id = id, EntryNumber = $"JE-{id}", EntryDate = at, Description = $"Sale {id}", ReferenceType = "Transaction",
                TotalAmount = amount, IsPosted = true,
                Lines =
                {
                    new JournalEntryLine { AccountId = 10, DebitAmount = amount, LineNumber = 1 },
                    new JournalEntryLine { AccountId = 40, CreditAmount = amount, LineNumber = 2 },
                },
            });

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Trial_balance_as_of_a_day_includes_that_whole_day_only()
        {
            var r = await _svc.GetTrialBalanceAsync(new DateTime(2026, 10, 6)); // as the page sends it

            r.Success.Should().BeTrue();
            var tb = r.Data!;
            tb.Lines.Single(l => l.AccountNumber == "1000").DebitBalance.Should().Be(150m);
            tb.Lines.Single(l => l.AccountNumber == "4100").CreditBalance.Should().Be(150m);
            tb.TotalDebits.Should().Be(tb.TotalCredits);
            tb.IsBalanced.Should().BeTrue();
        }

        [Fact]
        public async Task Trial_balance_without_a_date_is_everything_posted_so_far()
        {
            var now = DateTime.UtcNow;
            var tb = (await _svc.GetTrialBalanceAsync()).Data!;

            // Everything posted up to now; voided entries never count.
            var expected = new[] { (Day.AddDays(-1).AddHours(12), 100m), (Day.AddHours(18), 50m), (Day.AddDays(1).AddHours(9), 999m) }
                .Where(x => x.Item1 <= now).Sum(x => x.Item2);
            tb.Lines.Single(l => l.AccountNumber == "1000").DebitBalance.Should().Be(expected);
            tb.IsBalanced.Should().BeTrue();
        }

        [Fact]
        public async Task General_ledger_includes_the_whole_to_day()
        {
            var r = await _svc.GetGeneralLedgerAsync(10, new DateTime(2026, 10, 6), new DateTime(2026, 10, 6));

            r.Success.Should().BeTrue();
            var gl = r.Data!;
            gl.OpeningBalance.Should().Be(100m);
            gl.Transactions.Select(t => t.EntryNumber).Should().Equal("JE-2");
            gl.ClosingBalance.Should().Be(150m);
        }
    }
}
