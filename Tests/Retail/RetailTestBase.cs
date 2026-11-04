using Application.Common;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Tests.Retail
{
    internal sealed class RetailTestUser : ICurrentUserAccessor
    {
        public int? UserId => 1;
        public string UserName => "admin@axislb.com";
        public IReadOnlyList<string> Roles => new[] { "admin" };
        public int? BranchId => 1;
        public bool IsInRole(string role) => Roles.Contains(role);
    }

    internal static class RetailTestDb
    {
        public static ApplicationDbContext Create()
            => new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        /// <summary>Items 9 (booster, plain) and 10 (sleeves, Black #1 / Green #2), invoice 70 open.</summary>
        public static void SeedCatalog(ApplicationDbContext db)
        {
            db.AddRange(
                new Item { Id = 9, Name = "MTG Booster Pack", Quantity = 40, Price = 5m, Type = "CoffeeShop", CategoryId = 3, StatusId = 1 },
                new Item { Id = 10, Name = "Card Sleeves (100)", Quantity = 25, Price = 6m, Type = "CoffeeShop", CategoryId = 3, StatusId = 1 },
                new Item { Id = 11, Name = "Old Dice", Quantity = 3, Price = 2m, Type = "CoffeeShop", CategoryId = 3, StatusId = 2 },
                new ItemVariant { Id = 1, ItemId = 10, Name = "Black", Sku = "SLV-BLK", PriceDelta = 0m, Quantity = 15, IsActive = true, SortOrder = 1 },
                new ItemVariant { Id = 2, ItemId = 10, Name = "Green", Sku = "SLV-GRN", PriceDelta = 0.5m, Quantity = 10, IsActive = true, SortOrder = 2 },
                new TransactionRecord { Id = 70, StatusId = 7, TotalPrice = 17m, CreatedBy = "cashier@axislb.com", CreatedOn = DateTime.UtcNow },
                new TransactionRecord { Id = 71, StatusId = 7, TotalPrice = 9m, CreatedBy = "cashier@axislb.com", CreatedOn = DateTime.UtcNow });
            db.SaveChanges();
        }
    }
}
