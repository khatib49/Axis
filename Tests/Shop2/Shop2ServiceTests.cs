using Application.DTOs;
using Application.IServices;
using Application.Services.Shop2;
using Domain.Entities;
using Domain.Identity;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Tests.Shop2
{
    /// <summary>
    /// In-memory db seeded like the demo: two shop categories (Retail + FnB),
    /// one hidden category, a disabled item and an item flagged SellOnline=false.
    /// </summary>
    public sealed class Shop2Fixture : IDisposable
    {
        public ApplicationDbContext Db { get; }
        public Mock<IWhishInvoicePaymentService> Whish { get; } = new();
        public Shop2Service Svc { get; }

        public Shop2Fixture()
        {
            Db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options);
            Db.Add(new Status { Id = 1, Name = "Enabled" });
            Db.Add(new Status { Id = 6, Name = "Paid" });
            Db.Add(new Status { Id = 7, Name = "Open" });
            Db.Add(new AppUser { Id = 1, UserName = "+96170000001", PhoneNumber = "+96170000001", StatusId = 6 });
            Db.Add(new AppUser { Id = 2, UserName = "+96170000002", PhoneNumber = "+96170000002", StatusId = 6 });

            Db.Add(new Category { Id = 1, Name = "Food", Type = "item", ItemType = "FnB", ShowInShop = true });
            Db.Add(new Category { Id = 2, Name = "Drinks", Type = "item", ItemType = "FnB", ShowInShop = false });
            Db.Add(new Category { Id = 3, Name = "TCG & Retail", Type = "item", ItemType = "Retail", ShowInShop = true });
            Db.Add(new Category { Id = 4, Name = "Consoles", Type = "game", ItemType = null, ShowInShop = true });

            Db.Add(new Item { Type = "CoffeeShop", Id = 1, Name = "Axis Burger", Price = 9.5m, Quantity = 97, CategoryId = 1, StatusId = 1, SellOnline = true });
            Db.Add(new Item { Type = "CoffeeShop", Id = 2, Name = "Loaded Fries", Price = 5m, Quantity = 99, CategoryId = 1, StatusId = 1, SellOnline = true });
            Db.Add(new Item { Type = "CoffeeShop", Id = 3, Name = "Staff Meal", Price = 1m, Quantity = 5, CategoryId = 1, StatusId = 1, SellOnline = false });
            Db.Add(new Item { Type = "CoffeeShop", Id = 4, Name = "Old Special", Price = 3m, Quantity = 5, CategoryId = 1, StatusId = 2, SellOnline = true });
            Db.Add(new Item { Type = "CoffeeShop", Id = 5, Name = "Coke", Price = 2m, Quantity = 95, CategoryId = 2, StatusId = 1, SellOnline = true });
            Db.Add(new Item { Type = "CoffeeShop", Id = 9, Name = "MTG Booster Pack", Price = 5m, Quantity = 40, CategoryId = 3, StatusId = 1, SellOnline = true, ImagePath = "uploads/booster.png" });
            Db.Add(new Item { Type = "CoffeeShop", Id = 10, Name = "Card Sleeves (100)", Price = 6m, Quantity = 0, CategoryId = 3, StatusId = 1, SellOnline = true });
            Db.Add(new Item { Type = "CoffeeShop", Id = 11, Name = "Playmat", Price = 20m, Quantity = 0, CategoryId = 3, StatusId = 1, SellOnline = true });
            Db.Add(new ItemVariant { Id = 1, ItemId = 11, Name = "Black", Quantity = 3, IsActive = true, SortOrder = 1 });
            Db.Add(new ItemVariant { Id = 2, ItemId = 11, Name = "Red", Quantity = 0, IsActive = false, SortOrder = 0 });
            Db.Add(new ItemAddOn { Id = 1, ItemId = 1, Name = "Extra cheese", Price = 1m, IsActive = true, SortOrder = 0 });
            Db.Add(new ItemAddOn { Id = 2, ItemId = 1, Name = "Retired extra", Price = 1m, IsActive = false, SortOrder = 1 });
            Db.SaveChanges();

            Whish.Setup(w => w.StartAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int tx, string src, string ok, string ko, string? ret, CancellationToken _) =>
                    new BaseResponse<WhishStartResultDto>(true, null, "ok", new WhishStartResultDto(77, $"https://whish.test/{tx}", $"https://whish.test/{tx}", 14.5m, "USD", "Pending")));

            Svc = new Shop2Service(Db, Whish.Object, NullLogger<Shop2Service>.Instance);
        }

        public OnlineOrder AddOrder(string code, int userId, string status = "New", int? txId = null, string mode = "PayAtPickup")
        {
            var o = new OnlineOrder { Code = code, UserId = userId, CustomerName = "Test", CustomerPhone = "+961", Status = status, PaymentMode = mode, TransactionRecordId = txId, Subtotal = 14.5m, Total = 14.5m };
            Db.Add(o); Db.SaveChanges();
            return o;
        }

        public void AddTransaction(int id, decimal total, int statusId = 7)
        {
            Db.Add(new TransactionRecord { Id = id, StatusId = statusId, TotalPrice = total, CreatedBy = "web:cashier", UserId = 1 });
            Db.SaveChanges();
        }

        public void Dispose() => Db.Dispose();
    }

    public class Shop2ServiceTests
    {
        [Fact]
        public async Task Categories_only_shop_flagged_item_categories_with_sellable_counts()
        {
            using var f = new Shop2Fixture();
            var cats = await f.Svc.CategoriesAsync();

            cats.Select(c => c.Id).Should().BeEquivalentTo(new[] { 1, 3 }, "Drinks is not ShowInShop and Consoles is not an item category");
            cats.Single(c => c.Id == 1).ItemCount.Should().Be(2, "SellOnline=false and disabled items are excluded");
            cats.Single(c => c.Id == 3).ItemCount.Should().Be(3);
            cats.Single(c => c.Id == 3).CoverImagePath.Should().Be("uploads/booster.png");
            cats.Single(c => c.Id == 1).MinPrice.Should().Be(5m);
        }

        [Fact]
        public async Task Items_are_paged_and_never_include_hidden_items()
        {
            using var f = new Shop2Fixture();
            var p1 = await f.Svc.ItemsAsync(new ShopItemListRequest(Page: 1, PageSize: 2));
            var p3 = await f.Svc.ItemsAsync(new ShopItemListRequest(Page: 3, PageSize: 2));

            p1.TotalCount.Should().Be(5);
            p1.PageNumber.Should().Be(1); p1.PageSize.Should().Be(2);
            p1.Data.Select(i => i.Name).Should().Equal("Axis Burger", "Card Sleeves (100)");
            p3.Data.Should().HaveCount(1);
            var all = await f.Svc.ItemsAsync(new ShopItemListRequest(PageSize: 100));
            all.Data.Select(i => i.Id).Should().NotContain(new[] { 3, 4, 5 });
        }

        [Fact]
        public async Task Items_filter_by_category_search_and_stock_and_sort_by_price()
        {
            using var f = new Shop2Fixture();
            (await f.Svc.ItemsAsync(new ShopItemListRequest(CategoryId: 3))).TotalCount.Should().Be(3);
            (await f.Svc.ItemsAsync(new ShopItemListRequest(CategoryId: 2))).TotalCount.Should().Be(0, "hidden category");
            (await f.Svc.ItemsAsync(new ShopItemListRequest(Search: "booster"))).Data.Should().ContainSingle(i => i.Id == 9);
            (await f.Svc.ItemsAsync(new ShopItemListRequest(Search: "retail"))).TotalCount.Should().Be(3, "search also matches the category name");

            var inStock = await f.Svc.ItemsAsync(new ShopItemListRequest(CategoryId: 3, InStockOnly: true));
            inStock.Data.Select(i => i.Id).Should().BeEquivalentTo(new[] { 9, 11 }, "sleeves are sold out; the playmat has an in-stock variant");

            var desc = await f.Svc.ItemsAsync(new ShopItemListRequest(Sort: "priceDesc", PageSize: 100));
            desc.Data.Select(i => i.Price).Should().BeInDescendingOrder();
            var asc = await f.Svc.ItemsAsync(new ShopItemListRequest(Sort: "priceAsc", PageSize: 100));
            asc.Data.First().Name.Should().Be("Loaded Fries");
        }

        [Fact]
        public async Task Item_exposes_active_variants_and_addons_and_hides_unsellable()
        {
            using var f = new Shop2Fixture();
            var burger = await f.Svc.ItemAsync(1);
            burger.Should().NotBeNull();
            burger!.AddOns.Select(a => a.Id).Should().Equal(1);
            burger.TrackStock.Should().BeFalse("FnB items are made to order");
            burger.CategoryName.Should().Be("Food");

            var mat = await f.Svc.ItemAsync(11);
            mat!.Variants.Select(v => v.Name).Should().Equal("Black");
            mat.TrackStock.Should().BeTrue();

            (await f.Svc.ItemAsync(3)).Should().BeNull("SellOnline=false");
            (await f.Svc.ItemAsync(4)).Should().BeNull("disabled");
            (await f.Svc.ItemAsync(5)).Should().BeNull("category hidden");
            (await f.Svc.ItemAsync(999)).Should().BeNull();
        }

        [Fact]
        public async Task Featured_puts_pictured_in_stock_items_first_and_respects_take()
        {
            using var f = new Shop2Fixture();
            var rows = await f.Svc.FeaturedAsync(3);
            rows.Should().HaveCount(3);
            rows.First().Id.Should().Be(9, "the only item with a picture");
            (await f.Svc.FeaturedAsync(0)).Should().HaveCount(5, "0 falls back to the default of 8, capped by what exists");
        }

        [Fact]
        public async Task Invoice_lookup_is_guarded_by_owner_and_only_reports_existing_invoices()
        {
            using var f = new Shop2Fixture();
            f.AddTransaction(500, 14.5m, statusId: 7);
            f.AddTransaction(501, 14.5m, statusId: 6);
            f.AddOrder("AX-NEW1", userId: 1);                       // not accepted yet
            f.AddOrder("AX-OPEN", userId: 1, "Accepted", txId: 500);
            f.AddOrder("AX-PAID", userId: 1, "Completed", txId: 501);
            f.AddOrder("AX-OTHR", userId: 2, "Accepted", txId: 500);

            (await f.Svc.OrderInvoiceAsync(1, "AX-NEW1")).Should().BeNull("ShopService has not created an invoice yet and we never create one");
            (await f.Svc.OrderInvoiceAsync(2, "AX-OPEN")).Should().BeNull("another customer's order");
            (await f.Svc.OrderInvoiceAsync(1, "AX-NOPE")).Should().BeNull();
            (await f.Svc.OrderInvoiceAsync(1, "")).Should().BeNull();

            var open = await f.Svc.OrderInvoiceAsync(1, "AX-OPEN");
            open.Should().NotBeNull();
            open!.TransactionId.Should().Be(500);
            open.IsOpen.Should().BeTrue();
            open.InvoiceTotal.Should().Be(14.5m);
            open.OrderStatus.Should().Be("Accepted");

            var paid = await f.Svc.OrderInvoiceAsync(1, "AX-PAID");
            paid!.IsOpen.Should().BeFalse();
            f.Db.Set<TransactionRecord>().Count().Should().Be(2, "no invoice was created by the lookup");
        }

        [Fact]
        public async Task Whish_start_requires_own_order_with_an_open_invoice()
        {
            using var f = new Shop2Fixture();
            f.AddTransaction(600, 14.5m, statusId: 7);
            f.AddTransaction(601, 14.5m, statusId: 6);
            f.AddOrder("AX-NEW1", userId: 1);
            f.AddOrder("AX-OPEN", userId: 1, "Accepted", txId: 600);
            f.AddOrder("AX-PAID", userId: 1, "Completed", txId: 601);
            f.AddOrder("AX-CANC", userId: 1, "Cancelled", txId: 600);

            (await f.Svc.StartWhishAsync(2, "AX-OPEN", "ok", "ko", null)).Success.Should().BeFalse("not the owner");
            (await f.Svc.StartWhishAsync(1, "AX-NEW1", "ok", "ko", null)).Error.Should().Be("NoInvoice");
            (await f.Svc.StartWhishAsync(1, "AX-PAID", "ok", "ko", null)).Error.Should().Be("Closed");
            (await f.Svc.StartWhishAsync(1, "AX-CANC", "ok", "ko", null)).Error.Should().Be("Cancelled");
            f.Whish.Verify(w => w.StartAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

            var r = await f.Svc.StartWhishAsync(1, "AX-OPEN", "https://ok", "https://ko", "/shop/orders/AX-OPEN");
            r.Success.Should().BeTrue();
            r.Data!.RequestId.Should().Be(77);
            r.Data.CollectUrl.Should().Be("https://whish.test/600");
            f.Whish.Verify(w => w.StartAsync(600, "SelfOrder", "https://ok", "https://ko", "/shop/orders/AX-OPEN", It.IsAny<CancellationToken>()), Times.Once);
        }
    }
}
