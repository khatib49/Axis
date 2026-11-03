using Application.DTOs;
using Application.Services.StockOps;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Tests.StockOps
{
    public class StockBatchTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly StockBatchService _svc;
        private readonly DateTime _today = DateTime.UtcNow.Date;

        public StockBatchTests()
        {
            _db = StockOpsTestDb.Create();
            _db.AddRange(
                new Ingredient { Id = 1, Name = "Yogurt", Unit = "pcs", QuantityOnHand = 30m, BuyPricePerUnit = 2m },
                new StockBatch { Id = 1, IngredientId = 1, LotNumber = "EXP", Quantity = 10, RemainingQuantity = 4, ExpiresOn = _today.AddDays(-2) },
                new StockBatch { Id = 2, IngredientId = 1, LotNumber = "SOON", Quantity = 10, RemainingQuantity = 10, ExpiresOn = _today.AddDays(3) },
                new StockBatch { Id = 3, IngredientId = 1, LotNumber = "EDGE", Quantity = 10, RemainingQuantity = 10, ExpiresOn = _today.AddDays(7) },
                new StockBatch { Id = 4, IngredientId = 1, LotNumber = "LATER", Quantity = 10, RemainingQuantity = 10, ExpiresOn = _today.AddDays(8) },
                new StockBatch { Id = 5, IngredientId = 1, LotNumber = "EMPTY", Quantity = 10, RemainingQuantity = 0, ExpiresOn = _today.AddDays(1) },
                new StockBatch { Id = 6, IngredientId = 1, LotNumber = "NODATE", Quantity = 10, RemainingQuantity = 10, ExpiresOn = null });
            _db.SaveChanges();
            _svc = new StockBatchService(_db, new TestCurrentUser(), NullLogger<StockBatchService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Expiring_query_includes_the_window_edge_and_excludes_expired_empty_and_undated()
        {
            var r = await _svc.GetExpiringAsync(7);
            r.Select(b => b.LotNumber).Should().Equal("SOON", "EDGE");
            r.First().DaysToExpiry.Should().Be(3);
            r.Should().OnlyContain(b => !b.IsExpired);

            (await _svc.GetExpiringAsync(3)).Select(b => b.LotNumber).Should().Equal("SOON");
        }

        [Fact]
        public async Task Expired_query_lists_only_batches_with_remaining_quantity()
        {
            var r = await _svc.GetExpiredAsync();
            r.Select(b => b.LotNumber).Should().Equal("EXP");
            r[0].IsExpired.Should().BeTrue();
            r[0].DaysToExpiry.Should().Be(-2);
        }

        [Fact]
        public async Task Write_off_writes_waste_movement_for_remaining_and_zeroes_the_batch()
        {
            var r = await _svc.WriteOffExpiredAsync(1, null);
            r.Success.Should().BeTrue(r.Error);
            r.Data!.RemainingQuantity.Should().Be(0m);
            r.Data.WrittenOffOn.Should().NotBeNull();

            (await _db.Set<Ingredient>().FindAsync(1))!.QuantityOnHand.Should().Be(26m);
            var m = await _db.Set<StockMovement>().SingleAsync();
            m.Type.Should().Be("Waste");
            m.WasteReason.Should().Be("Expired");
            m.Quantity.Should().Be(-4m);
            m.BalanceAfter.Should().Be(26m);
            m.UnitCost.Should().Be(2m);
            m.TotalCost.Should().Be(8.00m);
            m.ReferenceType.Should().Be("StockBatch");
            m.ReferenceId.Should().Be(1);
            m.CreatedBy.Should().Be("stock@axislb.com");

            (await _svc.WriteOffExpiredAsync(1, null)).Success.Should().BeFalse("nothing left to write off");
            (await _svc.WriteOffExpiredAsync(2, null)).Success.Should().BeFalse("not expired yet");
            (await _db.Set<StockMovement>().CountAsync()).Should().Be(1);
        }

        [Fact]
        public async Task Receive_records_a_batch_without_moving_stock()
        {
            var r = await _svc.ReceiveAsync(new StockBatchCreateDto(1, 12m, _today.AddDays(10), LotNumber: "NEW"));
            r.Success.Should().BeTrue(r.Error);
            r.Data!.RemainingQuantity.Should().Be(12m);
            r.Data.DaysToExpiry.Should().Be(10);
            (await _db.Set<Ingredient>().FindAsync(1))!.QuantityOnHand.Should().Be(30m);
            (await _db.Set<StockMovement>().CountAsync()).Should().Be(0);
            (await _svc.ReceiveAsync(new StockBatchCreateDto(1, 0m, null))).Success.Should().BeFalse();
        }
    }
}
