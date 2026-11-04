using Application.DTOs;
using Application.IServices;
using Application.Services.Promotions;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Tests.Promotions
{
    public class ComboPriceTests : IDisposable
    {
        [Fact]
        public void Price_delta_is_negative_when_cheaper_and_zero_otherwise()
        {
            var items = ComboEngine.ItemsTotal(new[] { (10m, 1), (4m, 2) }); // burger + 2 fries = 18
            items.Should().Be(18m);
            ComboEngine.PriceDelta(15m, items).Should().Be(-3m);
            ComboEngine.PriceDelta(18m, items).Should().Be(0m);
            ComboEngine.PriceDelta(25m, items).Should().Be(0m, "a combo never charges more than its parts");
        }

        private readonly ApplicationDbContext _db = PromoDb.New();
        public void Dispose() => _db.Dispose();

        private PromotionAutoApplyService AutoApply() => PromoAccounting.AutoApply(_db);

        private static TransactionDto Dto(int id, decimal total) => new(
            id, null, "", null, "", null, "", null, "", 0, total, 7, DateTime.UtcNow, null, "seed",
            new List<TransactionItemDto>(), null, "", null, null, null, 1, false, null, null, null);

        [Fact]
        public async Task Sell_adds_lines_through_the_existing_order_path_then_books_the_saving()
        {
            PromoDb.SeedCatalogAndInvoices(_db);
            var combo = new Combo { Id = 1, Name = "Burger meal", Price = 12m, IsActive = true };
            combo.Items.Add(new ComboItem { ItemId = 2, Quantity = 1 }); // burger 10
            combo.Items.Add(new ComboItem { ItemId = 3, Quantity = 1 }); // fries 4 → items 14, saving 2
            _db.Add(combo);
            await _db.SaveChangesAsync();

            var tx = new Mock<ITransactionRecordService>(MockBehavior.Strict);
            List<OrderItemRequest>? sent = null;
            tx.Setup(t => t.AddItemsToOpenInvoice(10, It.IsAny<List<OrderItemRequest>>(), "tester@axislb.com", It.IsAny<CancellationToken>()))
              .Callback<int, List<OrderItemRequest>, string, CancellationToken>((_, items, _, _) =>
              {
                  sent = items;
                  // Mimic the real service: lines land on the invoice at full price.
                  var trx = _db.Transactions.Include(t => t.TransactionItems).Single(t => t.Id == 10);
                  foreach (var it in items)
                  {
                      var line = trx.TransactionItems.FirstOrDefault(l => l.ItemId == it.ItemId);
                      if (line is null) trx.TransactionItems.Add(new TransactionItem { TransactionRecordId = 10, ItemId = it.ItemId, Quantity = it.Quantity });
                      else line.Quantity += it.Quantity;
                  }
                  trx.TotalPrice += 14m;
                  _db.SaveChanges();
              })
              .ReturnsAsync(() => new BaseResponse<TransactionDto>(true, null, "ok", Dto(10, 34m)));

            var svc = new ComboService(_db, tx.Object,
                AutoApply(),
                new FakeUser(), NullLogger<ComboService>.Instance);

            var r = await svc.SellAsync(new ComboSellDto(1, 10));
            r.Success.Should().BeTrue(r.Message);
            sent.Should().BeEquivalentTo(new[] { new OrderItemRequest(2, 1), new OrderItemRequest(3, 1) });
            r.Data!.ItemsTotal.Should().Be(14m);
            r.Data.AdjustmentApplied.Should().Be(-2m);
            r.Data.InvoiceTotalAfter.Should().Be(32m);
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(32m);

            // Selling the same combo again on the same invoice books a second saving (new sequence key).
            var r2 = await svc.SellAsync(new ComboSellDto(1, 10));
            r2.Success.Should().BeTrue(r2.Message);
            r2.Data!.AdjustmentApplied.Should().Be(-2m);
            (await _db.Transactions.FindAsync(10))!.TotalPrice.Should().Be(44m);
            (await _db.TransactionAuditLogs.CountAsync(a => a.TransactionId == 10 && a.Action == "PromotionAdjust")).Should().Be(2);
            var app = await _db.Set<ComboApplication>().SingleAsync(a => a.TransactionId == 10 && a.ComboId == 1);
            app.Quantity.Should().Be(2);
            app.Amount.Should().Be(4m);
        }

        [Fact]
        public async Task Sell_refuses_closed_invoice_and_inactive_combo_without_touching_the_order_service()
        {
            PromoDb.SeedCatalogAndInvoices(_db);
            var active = new Combo { Id = 1, Name = "A", Price = 1m, IsActive = true };
            active.Items.Add(new ComboItem { ItemId = 1, Quantity = 1 });
            var inactive = new Combo { Id = 2, Name = "B", Price = 1m, IsActive = false };
            inactive.Items.Add(new ComboItem { ItemId = 1, Quantity = 1 });
            _db.AddRange(active, inactive);
            await _db.SaveChangesAsync();

            var tx = new Mock<ITransactionRecordService>(MockBehavior.Strict);
            var svc = new ComboService(_db, tx.Object,
                AutoApply(),
                new FakeUser(), NullLogger<ComboService>.Instance);

            (await svc.SellAsync(new ComboSellDto(1, 20))).Message.Should().Contain("open invoice");
            (await svc.SellAsync(new ComboSellDto(2, 10))).Error.Should().Be("Inactive");
            (await svc.SellAsync(new ComboSellDto(99, 10))).Error.Should().Be("Not found");
            tx.VerifyNoOtherCalls();
        }

        [Fact]
        public async Task Create_and_update_validate_items_and_report_the_saving()
        {
            PromoDb.SeedCatalogAndInvoices(_db);
            var tx = new Mock<ITransactionRecordService>();
            var svc = new ComboService(_db, tx.Object,
                AutoApply(),
                new FakeUser(), NullLogger<ComboService>.Instance);

            (await svc.CreateAsync(new ComboUpsertDto("Empty", null, 5m))).Message.Should().Contain("at least one item");
            (await svc.CreateAsync(new ComboUpsertDto("Bad", null, 5m, Items: new() { new(999, 1) }))).Message.Should().Contain("Unknown item");

            var created = await svc.CreateAsync(new ComboUpsertDto("Meal", "d", 12m, Items: new() { new(2, 1), new(3, 1) }));
            created.Success.Should().BeTrue(created.Message);
            created.Data!.ItemsTotal.Should().Be(14m);
            created.Data.Saving.Should().Be(2m);

            var updated = await svc.UpdateAsync(created.Data.Id, new ComboUpsertDto("Meal", "d", 12m, Items: new() { new(2, 2) }));
            updated.Success.Should().BeTrue(updated.Message);
            updated.Data!.Items.Should().ContainSingle(i => i.ItemId == 2 && i.Quantity == 2);
            updated.Data.ItemsTotal.Should().Be(20m);
        }
    }
}
