using Application.Services;
using Application.Services.MenuEngineering;
using Domain.Entities;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Tests.MenuEngineering
{
    internal static class TestDb
    {
        public static ApplicationDbContext New() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        /// <summary>Real ItemRevenueReportService on top of the in-memory context, exactly like production wiring.</summary>
        public static MenuEngineeringService Service(ApplicationDbContext db) => new(
            db,
            new ItemRevenueReportService(
                new BaseRepository<TransactionRecord>(db),
                new BaseRepository<Item>(db),
                new BaseRepository<Category>(db),
                new BaseRepository<RecipeLine>(db)),
            NullLogger<MenuEngineeringService>.Instance);

        public static readonly DateTime Day = new(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc);

        public static Category ItemCategory(int id, string name) => new() { Id = id, Name = name, Type = "item" };

        public static Item MenuItem(int id, string name, decimal price, int categoryId = 1, decimal? buyPrice = null) =>
            new() { Id = id, Name = name, Price = price, CategoryId = categoryId, StatusId = 1, Type = "CoffeeShop", Quantity = 0, BuyPrice = buyPrice };

        /// <summary>One paid invoice selling <paramref name="qty"/> of <paramref name="itemId"/> at list price.</summary>
        public static TransactionRecord Sale(int txId, int itemId, int qty, decimal unitPrice, DateTime? paidOn = null) => new()
        {
            Id = txId,
            StatusId = 6,
            TotalPrice = unitPrice * qty,
            CreatedOn = (paidOn ?? Day).AddHours(12),
            PaidOn = (paidOn ?? Day).AddHours(12),
            CreatedBy = "test",
            TransactionItems = new List<TransactionItem> { new() { TransactionRecordId = txId, ItemId = itemId, Quantity = qty } },
        };
    }
}
