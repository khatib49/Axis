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
    public class StockTransferTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly StockTransferService _svc;
        private readonly StockLocation _main;
        private readonly StockLocation _bar;

        public StockTransferTests()
        {
            _db = StockOpsTestDb.Create();
            _main = new StockLocation { Id = 1, Name = "Main", Code = "MAIN", IsDefault = true, IsActive = true };
            _bar = new StockLocation { Id = 2, Name = "Bar", Code = "BAR", IsActive = true };
            _db.AddRange(_main, _bar,
                new Ingredient { Id = 1, Name = "Vodka", Unit = "l", QuantityOnHand = 5m, BuyPricePerUnit = 20m });
            _db.SaveChanges();
            _svc = new StockTransferService(_db, new TestCurrentUser(), NullLogger<StockTransferService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Post_rejects_insufficient_quantity_at_source_and_changes_nothing()
        {
            var t = await _svc.CreateAsync(new StockTransferCreateDto(_main.Id, _bar.Id, null, new() { new(1, 8m) }));
            t.Success.Should().BeTrue(t.Error);

            var posted = await _svc.PostAsync(t.Data!.Id);
            posted.Success.Should().BeFalse();
            posted.Error.Should().Contain("Insufficient Vodka");

            (await _db.Set<Ingredient>().FindAsync(1))!.QuantityOnHand.Should().Be(5m);
            (await _db.Set<StockMovement>().CountAsync()).Should().Be(0);
            (await _db.Set<LocationStock>().CountAsync()).Should().Be(0);
            (await _db.Set<StockTransfer>().FindAsync(t.Data.Id))!.Status.Should().Be("Draft");
        }

        [Fact]
        public async Task Post_from_default_writes_negative_adjustment_and_fills_location_stock()
        {
            var t = (await _svc.CreateAsync(new StockTransferCreateDto(_main.Id, _bar.Id, "restock bar", new() { new(1, 2m) }))).Data!;
            var posted = await _svc.PostAsync(t.Id);
            posted.Success.Should().BeTrue(posted.Error);
            posted.Data!.Status.Should().Be("Posted");

            (await _db.Set<Ingredient>().FindAsync(1))!.QuantityOnHand.Should().Be(3m);
            var m = await _db.Set<StockMovement>().SingleAsync();
            m.Type.Should().Be("Adjustment");
            m.Quantity.Should().Be(-2m);
            m.BalanceAfter.Should().Be(3m);
            m.Notes.Should().StartWith("Transfer to Bar");
            m.ReferenceType.Should().Be("StockTransfer");
            m.ReferenceId.Should().Be(t.Id);

            var ls = await _db.Set<LocationStock>().SingleAsync();
            ls.LocationId.Should().Be(_bar.Id);
            ls.Quantity.Should().Be(2m);
        }

        [Fact]
        public async Task Post_into_default_writes_positive_adjustment_and_drains_location_stock()
        {
            _db.Add(new LocationStock { LocationId = _bar.Id, IngredientId = 1, Quantity = 1.5m });
            await _db.SaveChangesAsync();

            var t = (await _svc.CreateAsync(new StockTransferCreateDto(_bar.Id, _main.Id, null, new() { new(1, 1m) }))).Data!;
            var posted = await _svc.PostAsync(t.Id);
            posted.Success.Should().BeTrue(posted.Error);

            (await _db.Set<Ingredient>().FindAsync(1))!.QuantityOnHand.Should().Be(6m);
            var m = await _db.Set<StockMovement>().SingleAsync();
            m.Quantity.Should().Be(1m);
            m.BalanceAfter.Should().Be(6m);
            m.Notes.Should().StartWith("Transfer from Bar");
            (await _db.Set<LocationStock>().SingleAsync()).Quantity.Should().Be(0.5m);
        }

        [Fact]
        public async Task Same_location_and_empty_lines_are_rejected()
        {
            (await _svc.CreateAsync(new StockTransferCreateDto(_main.Id, _main.Id, null, new() { new(1, 1m) }))).Success.Should().BeFalse();
            (await _svc.CreateAsync(new StockTransferCreateDto(_main.Id, _bar.Id, null, new()))).Success.Should().BeFalse();
            (await _svc.CreateAsync(new StockTransferCreateDto(_main.Id, _bar.Id, null, new() { new(1, 0m) }))).Success.Should().BeFalse();
        }
    }
}
