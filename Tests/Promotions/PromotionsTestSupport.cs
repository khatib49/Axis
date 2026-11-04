using Application.Common;
using Application.IServices;
using Application.Mapping;
using Application.Services;
using Application.Services.AccountingPlus;
using Application.Services.Promotions;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.Promotions
{
    internal sealed class FakeUser : ICurrentUserAccessor
    {
        public int? UserId { get; init; } = 1;
        public string UserName { get; init; } = "tester@axislb.com";
        public IReadOnlyList<string> Roles { get; init; } = new[] { "admin" };
        public int? BranchId { get; init; }
        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    internal static class PromoDb
    {
        public static ApplicationDbContext New()
            => new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        /// <summary>Two categories, three items, an open invoice (#10) and a paid one (#20).</summary>
        public static void SeedCatalogAndInvoices(ApplicationDbContext db)
        {
            db.AddRange(
                new Status { Id = 6, Name = "Paid" },
                new Status { Id = 7, Name = "Open" },
                new Category { Id = 1, Name = "Coffee", Type = "coffee" },
                new Category { Id = 2, Name = "Food", Type = "food" },
                new Item { Id = 1, Name = "Latte", Price = 5m, CategoryId = 1, Type = "CoffeeShop", StatusId = 1, Quantity = 100 },
                new Item { Id = 2, Name = "Burger", Price = 10m, CategoryId = 2, Type = "CoffeeShop", StatusId = 1, Quantity = 100 },
                new Item { Id = 3, Name = "Fries", Price = 4m, CategoryId = 2, Type = "CoffeeShop", StatusId = 1, Quantity = 100 });

            // Open invoice: 2 lattes + 1 burger = 20
            db.Add(new TransactionRecord
            {
                Id = 10, StatusId = 7, TotalPrice = 20m, CreatedBy = "seed", CreatedOn = DateTime.UtcNow,
                TransactionItems = new List<TransactionItem>
                {
                    new() { TransactionRecordId = 10, ItemId = 1, Quantity = 2 },
                    new() { TransactionRecordId = 10, ItemId = 2, Quantity = 1 },
                },
            });
            // Paid invoice
            db.Add(new TransactionRecord { Id = 20, StatusId = 6, TotalPrice = 15m, CreatedBy = "seed", CreatedOn = DateTime.UtcNow, PaidOn = DateTime.UtcNow });
            db.SaveChanges();
        }

        /// <summary>Phase 3: the account types and the cash / sales accounts the transaction journal uses. 2150 / 4950 are NOT seeded (the service creates them).</summary>
        public static void SeedChartOfAccounts(ApplicationDbContext db)
        {
            db.AddRange(
                new AccountType { Id = 1, TypeName = "Asset", NormalBalance = "Debit" },
                new AccountType { Id = 2, TypeName = "Liability", NormalBalance = "Credit" },
                new AccountType { Id = 3, TypeName = "Equity", NormalBalance = "Credit" },
                new AccountType { Id = 4, TypeName = "Revenue", NormalBalance = "Credit" },
                new AccountType { Id = 5, TypeName = "Expense", NormalBalance = "Debit" });
            db.AddRange(
                new Account { Id = 1, AccountNumber = "1000", AccountName = "Cash on Hand", AccountTypeId = 1, IsSystemAccount = true },
                new Account { Id = 2, AccountNumber = "4100", AccountName = "FNB Revenue - Food", AccountTypeId = 4, IsSystemAccount = true, AllowManualEntry = false },
                new Account { Id = 3, AccountNumber = "4110", AccountName = "FNB Revenue - Beverages", AccountTypeId = 4, IsSystemAccount = true, AllowManualEntry = false });
            db.SaveChanges();
        }
    }

    /// <summary>Phase 3: the real JournalService / AccountService / AccountingPeriodService on the test db, like Tests/AccountingPlus does.</summary>
    internal static class PromoAccounting
    {
        public static JournalService Journal(ApplicationDbContext db) => new(
            new BaseRepository<JournalEntry>(db), new BaseRepository<JournalEntryLine>(db), new BaseRepository<Account>(db),
            new BaseRepository<TransactionRecord>(db), new BaseRepository<Expense>(db), new BaseRepository<Category>(db),
            new BaseRepository<EventRegistration>(db), new BaseRepository<OnlineOrder>(db), new UnitOfWork(db),
            new AccountingMapper(), NullLogger<JournalService>.Instance);

        public static AccountService Accounts(ApplicationDbContext db) => new(
            new BaseRepository<Account>(db), new BaseRepository<AccountType>(db), new BaseRepository<JournalEntryLine>(db),
            new BaseRepository<JournalEntry>(db), new UnitOfWork(db), new AccountingMapper(), NullLogger<AccountService>.Instance);

        public static AccountingPeriodService Periods(ApplicationDbContext db, ICurrentUserAccessor? user = null)
            => new(db, user ?? new FakeUser(), NullLogger<AccountingPeriodService>.Instance);

        public static PromotionAccountingService New(ApplicationDbContext db, ICurrentUserAccessor? user = null)
            => new(db, Journal(db), Accounts(db), Periods(db, user), user ?? new FakeUser(), NullLogger<PromotionAccountingService>.Instance);

        public static PromotionInvoiceAdjuster Adjuster(ApplicationDbContext db)
            => new(db, new FakeUser(), NullLogger<PromotionInvoiceAdjuster>.Instance);

        public static GiftCardService GiftCards(ApplicationDbContext db, PromotionAccountingService? acct = null)
            => new(db, Adjuster(db), acct ?? New(db), new FakeUser(), NullLogger<GiftCardService>.Instance);

        public static CouponService Coupons(ApplicationDbContext db, PromotionAccountingService? acct = null)
            => new(db, Adjuster(db), acct ?? New(db), new FakeUser(), NullLogger<CouponService>.Instance);

        public static PromotionAutoApplyService AutoApply(ApplicationDbContext db, PromotionAccountingService? acct = null)
            => new(db, Adjuster(db), acct ?? New(db), new FakeUser(), NullLogger<PromotionAutoApplyService>.Instance);
    }
}
