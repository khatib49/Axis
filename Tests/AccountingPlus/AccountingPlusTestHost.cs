using Application.Common;
using Application.IServices;
using Application.Mapping;
using Application.Services;
using Application.Services.AccountingPlus;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.AccountingPlus
{
    /// <summary>In-memory DB with a minimal chart of accounts and the real JournalService wired up.</summary>
    public sealed class AccountingPlusTestHost : IDisposable
    {
        public ApplicationDbContext Db { get; }
        public IJournalService Journal { get; }
        public AccountingPeriodService Periods { get; }
        public SupplierBillService Bills { get; }
        public FixedAssetService Assets { get; }
        public BankReconciliationService Bank { get; }
        public TaxService Tax { get; }
        public ReceivablesService Receivables { get; }
        public FakeUser User { get; } = new();

        public const int Cash = 1, BankGl = 2, Payable = 3, Expense = 4, AccumDep = 5, DepExpense = 6, AssetGl = 7, GainLoss = 8;
        public int? VatInput { get; private set; }

        public AccountingPlusTestHost(bool withVatInputAccount = false)
        {
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

            var asset = new AccountType { Id = 1, TypeName = "Asset", NormalBalance = "Debit" };
            var liability = new AccountType { Id = 2, TypeName = "Liability", NormalBalance = "Credit" };
            var expense = new AccountType { Id = 5, TypeName = "Expense", NormalBalance = "Debit" };
            var revenue = new AccountType { Id = 4, TypeName = "Revenue", NormalBalance = "Credit" };
            Db.AddRange(asset, liability, expense, revenue);
            Db.AddRange(
                new Account { Id = Cash, AccountNumber = "1000", AccountName = "Cash on Hand", AccountTypeId = 1 },
                new Account { Id = BankGl, AccountNumber = "1100", AccountName = "Bank Account - Primary", AccountTypeId = 1 },
                new Account { Id = Payable, AccountNumber = "2000", AccountName = "Accounts Payable", AccountTypeId = 2 },
                new Account { Id = Expense, AccountNumber = "5900", AccountName = "Miscellaneous Expense", AccountTypeId = 5 },
                new Account { Id = AccumDep, AccountNumber = "1600", AccountName = "Accumulated Depreciation", AccountTypeId = 1 },
                new Account { Id = DepExpense, AccountNumber = "5700", AccountName = "Depreciation Expense", AccountTypeId = 5 },
                new Account { Id = AssetGl, AccountNumber = "1500", AccountName = "Gaming Equipment", AccountTypeId = 1 },
                new Account { Id = GainLoss, AccountNumber = "4800", AccountName = "Gain/Loss on Disposal", AccountTypeId = 4 });
            if (withVatInputAccount)
            {
                VatInput = 9;
                Db.Add(new Account { Id = 9, AccountNumber = "1250", AccountName = "VAT Input", AccountTypeId = 1 });
            }
            Db.AddRange(new Supplier { Id = 1, Name = "Fresh Farms" }, new Supplier { Id = 2, Name = "Beirut Dairy" });
            Db.SaveChanges();

            Journal = new JournalService(
                new BaseRepository<JournalEntry>(Db), new BaseRepository<JournalEntryLine>(Db), new BaseRepository<Account>(Db),
                new BaseRepository<TransactionRecord>(Db), new BaseRepository<Expense>(Db), new BaseRepository<Category>(Db),
                new BaseRepository<EventRegistration>(Db), new BaseRepository<OnlineOrder>(Db), new UnitOfWork(Db),
                new AccountingMapper(), NullLogger<JournalService>.Instance);

            Periods = new AccountingPeriodService(Db, User, NullLogger<AccountingPeriodService>.Instance);
            Bills = new SupplierBillService(Db, Journal, Periods, User, NullLogger<SupplierBillService>.Instance);
            Assets = new FixedAssetService(Db, Journal, Periods, User, NullLogger<FixedAssetService>.Instance);
            Bank = new BankReconciliationService(Db, Periods, User, NullLogger<BankReconciliationService>.Instance);
            Tax = new TaxService(Db);
            Receivables = new ReceivablesService(Db);
        }

        /// <summary>Creates and posts a journal entry on the bank GL account against cash.</summary>
        public async Task<JournalEntryLine> PostBankMovementAsync(DateTime date, decimal amount, string desc = "bank move")
        {
            var lines = amount >= 0
                ? new List<Application.DTOs.JournalEntryLineCreateDto> { new(BankGl, amount, 0, desc), new(Cash, 0, amount, desc) }
                : new List<Application.DTOs.JournalEntryLineCreateDto> { new(BankGl, 0, -amount, desc), new(Cash, -amount, 0, desc) };
            var je = await Journal.CreateJournalEntryAsync(new Application.DTOs.JournalEntryCreateDto(date, desc, "Adjustment", null, lines), 1);
            if (!je.Success) throw new InvalidOperationException(je.Error);
            var posted = await Journal.PostJournalEntryAsync(je.Data!.Id, 1);
            if (!posted.Success) throw new InvalidOperationException(posted.Error);
            return await Db.Set<JournalEntryLine>().AsNoTracking().FirstAsync(l => l.JournalEntryId == je.Data.Id && l.AccountId == BankGl);
        }

        public void Dispose() => Db.Dispose();

        public sealed class FakeUser : ICurrentUserAccessor
        {
            public int? UserId => 1;
            public string UserName => "tester@axislb.com";
            public IReadOnlyList<string> Roles => new[] { "admin" };
            public int? BranchId => null;
            public bool IsInRole(string role) => Roles.Contains(role);
        }
    }
}
