using Application.Common;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
    }
}
