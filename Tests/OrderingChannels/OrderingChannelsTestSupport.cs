using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace Tests.OrderingChannels
{
    /// <summary>Shared fixtures: in-memory context, a fake till user and a transaction-service mock.</summary>
    internal static class OrderingChannelsTestSupport
    {
        public static ApplicationDbContext NewDb() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);

        public static Mock<ICurrentUserAccessor> User(int? userId = 10, string name = "cashier@axislb.com", int? branchId = null)
        {
            var m = new Mock<ICurrentUserAccessor>();
            m.SetupGet(u => u.UserId).Returns(userId);
            m.SetupGet(u => u.UserName).Returns(name);
            m.SetupGet(u => u.BranchId).Returns(branchId);
            m.SetupGet(u => u.Roles).Returns(new List<string> { "cashier" });
            return m;
        }

        public static TransactionDto Trx(int id, decimal total, int statusId = 7, string? comment = null, List<TransactionItemDto>? items = null, int? channelId = null) =>
            new(id, null, "", null, "", null, "", null, "", 0, total, statusId, DateTime.UtcNow, null, "test",
                items ?? new List<TransactionItemDto>(), null, "", null, null, null, 1, false, comment, null, null, channelId, null);

        public static TransactionItemDto Line(int itemId, string name, int qty, decimal price) =>
            new(itemId, name, qty, price, "CoffeeShop", new List<CoffeeShopOrderDto>());

        /// <summary>
        /// A transaction-service mock whose CreateCoffeeShopOrder answers with a fresh
        /// invoice id, computed from the seeded item prices, and records every call.
        /// </summary>
        public static Mock<ITransactionRecordService> TrxService(ApplicationDbContext db, List<(List<OrderItemRequest> items, string comment, bool open, int? channelId)> calls)
        {
            var next = 1000;
            var m = new Mock<ITransactionRecordService>();
            m.Setup(s => s.CreateCoffeeShopOrder(
                    It.IsAny<int?>(), It.IsAny<int>(), It.IsAny<List<OrderItemRequest>>(), It.IsAny<string>(), It.IsAny<CancellationToken>(),
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<decimal>()))
                .Returns((int? userId, int discountId, List<OrderItemRequest> items, string by, CancellationToken ct,
                          string comment, bool open, int? setId, int? channelId, decimal wallet) =>
                {
                    calls.Add((items, comment, open, channelId));
                    var prices = db.Set<Item>().AsNoTracking().ToDictionary(i => i.Id, i => i.Price);
                    var total = items.Sum(i => (prices.TryGetValue(i.ItemId, out var p) ? p : 0m) * i.Quantity);
                    var id = next++;
                    var trx = new TransactionRecord { Id = id, StatusId = open ? 7 : 6, TotalPrice = total, CreatedBy = by, Comment = comment, ChannelId = channelId };
                    foreach (var line in items)
                        trx.TransactionItems.Add(new TransactionItem { TransactionRecordId = id, ItemId = line.ItemId, Quantity = line.Quantity });
                    db.Add(trx);
                    db.SaveChanges();
                    return Task.FromResult(new BaseResponse<TransactionDto>(true, null, "ok", Trx(id, total, trx.StatusId, comment, channelId: channelId)));
                });
            return m;
        }

        public static void SeedMenu(ApplicationDbContext db)
        {
            var cat = new Category { Id = 1, Name = "Coffee", Type = "item", ItemType = "Food" };
            db.Add(cat);
            db.Add(new Item { Id = 1, Name = "Latte", Price = 5m, Quantity = 100, Type = "CoffeeShop", CategoryId = 1, StatusId = 1 });
            db.Add(new Item { Id = 2, Name = "Croissant", Price = 3m, Quantity = 100, Type = "CoffeeShop", CategoryId = 1, StatusId = 1 });
            db.Add(new Item { Id = 3, Name = "Burger", Price = 12m, Quantity = 100, Type = "CoffeeShop", CategoryId = 1, StatusId = 1 });
            db.SaveChanges();
            db.ChangeTracker.Clear();
        }
    }
}
