using Application.DTOs;
using Application.IServices;
using Application.Services;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Tests.Accounting
{
    /// <summary>
    /// Owners' drawings are contra-equity, never expenses: each owner has a
    /// sub-account under the Owners' Drawings header, every drawing posts
    /// DR owner drawings / CR 1000 Cash, and the summary splits the period's
    /// total by owner against their ownership %.
    /// </summary>
    public class OwnerDrawingsTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly OwnerService _svc;
        private int _entrySeq;

        public OwnerDrawingsTests()
        {
            _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            var asset = new AccountType { Id = 1, TypeName = "Asset", NormalBalance = "Debit", DisplayOrder = 1 };
            var equity = new AccountType { Id = 3, TypeName = "Equity", NormalBalance = "Credit", DisplayOrder = 3 };
            var expense = new AccountType { Id = 5, TypeName = "Expense", NormalBalance = "Debit", DisplayOrder = 5 };
            _db.AccountTypes.AddRange(asset, equity, expense);
            _db.Accounts.AddRange(
                new Account { Id = 10, AccountNumber = "1000", AccountName = "Cash on Hand", AccountTypeId = 1 },
                new Account { Id = 30, AccountNumber = "3000", AccountName = "Owner's Capital", AccountTypeId = 3 },
                new Account { Id = 31, AccountNumber = "3150", AccountName = "Omar old drawings", AccountTypeId = 3 },
                new Account { Id = 50, AccountNumber = "5100", AccountName = "Rent", AccountTypeId = 5 });
            _db.SaveChanges();

            var journal = new Mock<IJournalService>();
            journal.Setup(j => j.GenerateEntryNumberAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => $"JE-T-{++_entrySeq:D5}");

            _svc = new OwnerService(
                new BaseRepository<Owner>(_db), new BaseRepository<OwnerDrawing>(_db),
                new BaseRepository<Account>(_db), new BaseRepository<AccountType>(_db),
                new BaseRepository<JournalEntry>(_db), new BaseRepository<JournalEntryLine>(_db),
                new BaseRepository<ExpenseCategory>(_db), new BaseRepository<Expense>(_db),
                new BaseRepository<IntegrationSetting>(_db),
                journal.Object, new UnitOfWork(_db), NullLogger<OwnerService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        private async Task<Dictionary<string, OwnerDto>> SeedPartnersAsync()
        {
            var list = new[] { ("Ahmad Houhou", 50m), ("Mostafa", 15m), ("Mohamad", 15m), ("Omar", 20m) };
            var result = new Dictionary<string, OwnerDto>();
            foreach (var (name, pct) in list)
                result[name] = await _svc.CreateAsync(new OwnerCreateDto(name, pct, null, null), 1);
            return result;
        }

        // First day of last month: always in the past, with room for +3 days.
        private static readonly DateTime Day = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1).AddMonths(-1);

        private Account Acc(string number) => _db.Accounts.AsNoTracking().Include(a => a.AccountType).Single(a => a.AccountNumber == number);

        [Fact]
        public async Task Owners_get_one_equity_sub_account_each_under_a_non_postable_header()
        {
            var owners = await SeedPartnersAsync();

            var header = Acc("3300");
            header.AccountName.Should().Be("Owners' Drawings");
            header.AccountType.TypeName.Should().Be("Equity");
            header.AllowManualEntry.Should().BeFalse();

            owners.Values.Select(o => o.DrawingsAccountNumber).Should().BeEquivalentTo(new[] { "3310", "3320", "3330", "3340" });
            foreach (var o in owners.Values)
            {
                var acc = Acc(o.DrawingsAccountNumber);
                acc.ParentAccountId.Should().Be(header.Id);
                acc.AccountType.TypeName.Should().Be("Equity");
                acc.AllowManualEntry.Should().BeTrue();
            }
        }

        [Fact]
        public async Task Total_ownership_cannot_exceed_100_percent()
        {
            await SeedPartnersAsync();

            var act = () => _svc.CreateAsync(new OwnerCreateDto("Fifth partner", 1m, null, null), 1);
            await act.Should().ThrowAsync<ArgumentException>().WithMessage("*at most 0%*");

            var omar = (await _svc.ListAsync(false)).Single(o => o.Name == "Omar");
            var raise = () => _svc.UpdateAsync(omar.Id, new OwnerUpdateDto("Omar", 21m, null, true), 1);
            await raise.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task Drawing_posts_debit_owner_drawings_credit_cash_and_never_an_expense_account()
        {
            var owners = await SeedPartnersAsync();
            var omar = owners["Omar"];

            var d = await _svc.CreateDrawingAsync(new OwnerDrawingCreateDto(omar.Id, 600m, Day, "Cash", "Personal"), 1);

            var entry = _db.JournalEntries.AsNoTracking().Include(e => e.Lines).Single(e => e.Id == d.JournalEntryId);
            entry.ReferenceType.Should().Be("OwnerDrawing");
            entry.ReferenceId.Should().Be(d.Id);
            entry.IsPosted.Should().BeTrue();
            entry.Lines.Sum(l => l.DebitAmount).Should().Be(entry.Lines.Sum(l => l.CreditAmount));
            entry.Lines.Single(l => l.DebitAmount > 0).AccountId.Should().Be(omar.DrawingsAccountId);
            entry.Lines.Single(l => l.CreditAmount > 0).AccountId.Should().Be(10);
            entry.Lines.Should().NotContain(l => l.AccountId == 50);

            // Cached balances signed by normal balance: equity (credit-normal)
            // goes negative = debit balance, cash goes down.
            Acc(omar.DrawingsAccountNumber).CurrentBalance.Should().Be(-600m);
            Acc("1000").CurrentBalance.Should().Be(-600m);
        }

        [Fact]
        public async Task Summary_shows_each_owner_share_of_drawings_against_ownership()
        {
            var o = await SeedPartnersAsync();
            await _svc.CreateDrawingAsync(new OwnerDrawingCreateDto(o["Ahmad Houhou"].Id, 1000m, Day, null, null), 1);
            await _svc.CreateDrawingAsync(new OwnerDrawingCreateDto(o["Omar"].Id, 400m, Day, null, null), 1);
            await _svc.CreateDrawingAsync(new OwnerDrawingCreateDto(o["Omar"].Id, 200m, Day.AddDays(3), null, null), 1);
            await _svc.CreateDrawingAsync(new OwnerDrawingCreateDto(o["Mostafa"].Id, 400m, Day, null, null), 1);
            // Outside the period: counts in lifetime only.
            await _svc.CreateDrawingAsync(new OwnerDrawingCreateDto(o["Mohamad"].Id, 999m, Day.AddMonths(-2), null, null), 1);

            var s = await _svc.GetSummaryAsync(Day, Day.AddMonths(1).AddDays(-1));

            s.HeaderAccountNumber.Should().Be("3300");
            s.TotalDrawings.Should().Be(2000m);
            s.LifetimeTotalDrawings.Should().Be(2999m);
            s.TotalOwnershipPercent.Should().Be(100m);

            var ahmad = s.Owners.Single(x => x.Name == "Ahmad Houhou");
            ahmad.Drawn.Should().Be(1000m);
            ahmad.ShareOfDrawingsPercent.Should().Be(50m);
            ahmad.EntitledAmount.Should().Be(1000m);
            ahmad.Variance.Should().Be(0m);

            var omar = s.Owners.Single(x => x.Name == "Omar");
            omar.Drawn.Should().Be(600m);
            omar.EntryCount.Should().Be(2);
            omar.ShareOfDrawingsPercent.Should().Be(30m);
            omar.EntitledAmount.Should().Be(400m);
            omar.Variance.Should().Be(200m);

            var mostafa = s.Owners.Single(x => x.Name == "Mostafa");
            mostafa.ShareOfDrawingsPercent.Should().Be(20m);
            mostafa.Variance.Should().Be(100m);

            var mohamad = s.Owners.Single(x => x.Name == "Mohamad");
            mohamad.Drawn.Should().Be(0m);
            mohamad.EntitledAmount.Should().Be(300m);
            mohamad.Variance.Should().Be(-300m);
            mohamad.LifetimeDrawn.Should().Be(999m);

            s.Owners.Sum(x => x.Drawn).Should().Be(s.TotalDrawings);
        }

        [Fact]
        public async Task Cancelling_a_drawing_removes_it_from_the_ledger_and_restores_balances()
        {
            var o = await SeedPartnersAsync();
            var keep = await _svc.CreateDrawingAsync(new OwnerDrawingCreateDto(o["Omar"].Id, 100m, Day, null, null), 1);
            var drop = await _svc.CreateDrawingAsync(new OwnerDrawingCreateDto(o["Omar"].Id, 250m, Day, null, null), 1);

            var voided = await _svc.VoidDrawingAsync(drop.Id, "typed twice", 1);

            voided.IsVoided.Should().BeTrue();
            _db.JournalEntries.AsNoTracking().Single(e => e.Id == drop.JournalEntryId).IsVoided.Should().BeTrue();
            (await _svc.GetSummaryAsync(null, null)).Owners.Single(x => x.Name == "Omar").Drawn.Should().Be(100m);
            Acc(o["Omar"].DrawingsAccountNumber).CurrentBalance.Should().Be(-100m);
            Acc("1000").CurrentBalance.Should().Be(-100m);
            (await _svc.GetLifetimeDrawingsCashOutAsync()).Should().Be(100m);

            var again = () => _svc.VoidDrawingAsync(drop.Id, null, 1);
            await again.Should().ThrowAsync<InvalidOperationException>();
            keep.IsVoided.Should().BeFalse();
        }

        [Fact]
        public async Task Editing_a_drawing_reposts_it_to_the_new_owner()
        {
            var o = await SeedPartnersAsync();
            var d = await _svc.CreateDrawingAsync(new OwnerDrawingCreateDto(o["Omar"].Id, 300m, Day, null, null), 1);

            var edited = await _svc.UpdateDrawingAsync(d.Id, new OwnerDrawingUpdateDto(o["Mostafa"].Id, 350m, Day, "Cash", null), 1);

            edited.JournalEntryId.Should().NotBe(d.JournalEntryId);
            _db.JournalEntries.AsNoTracking().Single(e => e.Id == d.JournalEntryId).IsVoided.Should().BeTrue();
            var s = await _svc.GetSummaryAsync(null, null);
            s.Owners.Single(x => x.Name == "Omar").Drawn.Should().Be(0m);
            s.Owners.Single(x => x.Name == "Mostafa").Drawn.Should().Be(350m);
            s.TotalDrawings.Should().Be(350m);
            Acc("1000").CurrentBalance.Should().Be(-350m);
        }

        [Fact]
        public async Task Linking_an_existing_equity_account_moves_it_under_the_header_and_reports_old_categories()
        {
            // Legacy workaround: "Omar cash out" entries mapped straight to 3000 Owner's Capital.
            _db.Set<ExpenseCategory>().Add(new ExpenseCategory { Id = 7, Name = "Omar cash out", AccountId = 30 });
            _db.Expenses.Add(new Expense { FK_CategoryId = 7, Amount = 500m, FromDate = Day, ToDate = Day });
            _db.SaveChanges();

            var omar = await _svc.CreateAsync(new OwnerCreateDto("Omar", 20m, null, ExistingAccountId: 31), 1);

            omar.DrawingsAccountNumber.Should().Be("3150");
            Acc("3150").ParentAccountId.Should().Be(Acc("3300").Id);

            var s = await _svc.GetSummaryAsync(null, null);
            s.UnlinkedEquityCategories.Should().ContainSingle(c => c.CategoryName == "Omar cash out" && c.TotalAmount == 500m);

            var notEquity = () => _svc.CreateAsync(new OwnerCreateDto("Someone", 10m, null, ExistingAccountId: 50), 1);
            await notEquity.Should().ThrowAsync<ArgumentException>().WithMessage("*Equity*");
        }

        [Fact]
        public async Task Ledger_lists_every_entry_behind_the_summary_row_with_its_source()
        {
            var o = await SeedPartnersAsync();
            var omar = o["Omar"];
            await _svc.CreateDrawingAsync(new OwnerDrawingCreateDto(omar.Id, 400m, Day, "Cash", "Rent at home"), 1);

            // Legacy route: an "Omar cash out" entry posted through the expense pipeline.
            _db.Set<ExpenseCategory>().Add(new ExpenseCategory { Id = 8, Name = "Omar cash out", AccountId = omar.DrawingsAccountId });
            _db.Expenses.Add(new Expense { Id = 80, FK_CategoryId = 8, Amount = 150m, FromDate = Day.AddDays(1), ToDate = Day.AddDays(1), Comment = "Fuel" });
            _db.JournalEntries.Add(new JournalEntry
            {
                EntryNumber = "JE-T-90000", EntryDate = Day.AddDays(1), Description = "Fuel", ReferenceType = "Expense", ReferenceId = 80,
                TotalAmount = 150m, IsPosted = true,
                Lines =
                {
                    new JournalEntryLine { AccountId = omar.DrawingsAccountId, DebitAmount = 150m, LineNumber = 1 },
                    new JournalEntryLine { AccountId = 10, CreditAmount = 150m, LineNumber = 2 },
                },
            });
            _db.SaveChanges();

            var from = Day;
            var to = Day.AddMonths(1).AddDays(-1);
            var ledger = await _svc.GetAccountLedgerAsync(omar.DrawingsAccountId, from, to);
            var row = (await _svc.GetSummaryAsync(from, to)).Owners.Single(x => x.Name == "Omar");

            ledger.OwnerName.Should().Be("Omar");
            ledger.Drawn.Should().Be(550m).And.Be(row.Drawn);
            ledger.EntryCount.Should().Be(row.EntryCount);
            ledger.Lines.Select(l => l.Source).Should().Equal("Drawings page", "Entry category");
            ledger.Lines[0].SourceDetail.Should().Be("Cash · Rent at home");
            ledger.Lines[1].SourceDetail.Should().Be("Omar cash out · Fuel");
            ledger.Lines.Last().RunningTotal.Should().Be(550m);

            // Not a general-ledger backdoor: only accounts under the header.
            var outside = () => _svc.GetAccountLedgerAsync(10, from, to);
            await outside.Should().ThrowAsync<ArgumentException>();
        }

        [Fact]
        public async Task Hidden_owner_cannot_take_new_drawings()
        {
            var o = await SeedPartnersAsync();
            (await _svc.DeactivateAsync(o["Mohamad"].Id)).Should().BeTrue();

            var act = () => _svc.CreateDrawingAsync(new OwnerDrawingCreateDto(o["Mohamad"].Id, 10m, Day, null, null), 1);
            await act.Should().ThrowAsync<ArgumentException>();
            (await _svc.ListAsync(false)).Should().HaveCount(3);
        }
    }
}
