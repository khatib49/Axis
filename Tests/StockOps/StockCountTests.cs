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
    public class StockCountTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly StockCountService _svc;

        public StockCountTests()
        {
            _db = StockOpsTestDb.Create();
            _db.AddRange(
                new Ingredient { Id = 1, Name = "Beef", Unit = "kg", QuantityOnHand = 10m, BuyPricePerUnit = 12.5m },
                new Ingredient { Id = 2, Name = "Buns", Unit = "pcs", QuantityOnHand = 40m, BuyPricePerUnit = 0.5m },
                new Ingredient { Id = 3, Name = "Old Sauce", Unit = "l", QuantityOnHand = 5m, IsActive = false });
            _db.SaveChanges();
            _svc = new StockCountService(_db, new TestCurrentUser(), NullLogger<StockCountService>.Instance);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Draft_seeds_default_location_and_prefills_active_ingredients_with_system_qty()
        {
            var r = await _svc.CreateDraftAsync(new StockCountCreateDto(null, "monthly"));

            r.Success.Should().BeTrue(r.Error);
            r.Data!.Lines.Should().HaveCount(2);                // inactive ingredient excluded
            r.Data.Lines.Single(l => l.IngredientId == 1).ExpectedQty.Should().Be(10m);
            r.Data.Reference.Should().Be("SC-00001");
            var loc = await _db.Set<StockLocation>().SingleAsync();
            loc.IsDefault.Should().BeTrue();
            loc.Name.Should().Be("Main");
            r.Data.LocationId.Should().Be(loc.Id);
        }

        [Fact]
        public async Task Post_computes_variance_updates_quantity_on_hand_and_writes_adjustment_movements()
        {
            var draft = (await _svc.CreateDraftAsync(new StockCountCreateDto(null, null))).Data!;
            var beef = draft.Lines.Single(l => l.IngredientId == 1);
            var buns = draft.Lines.Single(l => l.IngredientId == 2);

            // Beef: counted 8 of 10 => -2 (shrink, -25.00). Buns: counted 40 => 0 variance (no movement).
            var upd = await _svc.UpdateLinesAsync(draft.Id, new StockCountLinesUpdateDto(new()
            {
                new(beef.Id, 8m), new(buns.Id, 40m)
            }));
            upd.Success.Should().BeTrue(upd.Error);

            var posted = await _svc.PostAsync(draft.Id);
            posted.Success.Should().BeTrue(posted.Error);
            posted.Data!.Status.Should().Be("Posted");

            var beefLine = posted.Data.Lines.Single(l => l.IngredientId == 1);
            beefLine.Variance.Should().Be(-2m);
            beefLine.UnitCost.Should().Be(12.5m);
            beefLine.VarianceValue.Should().Be(-25.00m);

            (await _db.Set<Ingredient>().FindAsync(1))!.QuantityOnHand.Should().Be(8m);
            (await _db.Set<Ingredient>().FindAsync(2))!.QuantityOnHand.Should().Be(40m);

            var movements = await _db.Set<StockMovement>().ToListAsync();
            movements.Should().HaveCount(1);
            var m = movements[0];
            m.IngredientId.Should().Be(1);
            m.Type.Should().Be("Adjustment");
            m.Quantity.Should().Be(-2m);
            m.BalanceAfter.Should().Be(8m);
            m.UnitCost.Should().Be(12.5m);
            m.TotalCost.Should().Be(-25.00m);
            m.Notes.Should().Be("Stocktake #SC-00001");
            m.ReferenceType.Should().Be("StockCount");
            m.ReferenceId.Should().Be(draft.Id);
            m.CreatedBy.Should().Be("stock@axislb.com");

            var report = await _svc.VarianceReportAsync(draft.Id);
            report.Success.Should().BeTrue();
            report.Data!.TotalShrinkValue.Should().Be(25.00m);
            report.Data.TotalSurplusValue.Should().Be(0m);
            report.Data.NetVarianceValue.Should().Be(-25.00m);
            report.Data.TopVariances.Should().ContainSingle(l => l.IngredientId == 1);
        }

        [Fact]
        public async Task Post_uses_live_quantity_so_the_final_balance_equals_the_count()
        {
            var draft = (await _svc.CreateDraftAsync(new StockCountCreateDto(null, null))).Data!;
            var beef = draft.Lines.Single(l => l.IngredientId == 1);
            await _svc.UpdateLinesAsync(draft.Id, new StockCountLinesUpdateDto(new() { new(beef.Id, 9m) }));

            // A sale happens while the sheet is open: 10 -> 7.
            var ing = await _db.Set<Ingredient>().FindAsync(1);
            ing!.QuantityOnHand = 7m;
            await _db.SaveChangesAsync();

            var posted = await _svc.PostAsync(draft.Id);
            posted.Success.Should().BeTrue(posted.Error);
            var line = posted.Data!.Lines.Single(l => l.IngredientId == 1);
            line.ExpectedQty.Should().Be(7m);
            line.Variance.Should().Be(2m);
            (await _db.Set<Ingredient>().FindAsync(1))!.QuantityOnHand.Should().Be(9m);
        }

        [Fact]
        public async Task Post_at_non_default_location_moves_location_stock_only()
        {
            await StockOpsSupport.EnsureDefaultLocationAsync(_db, null, default);
            var bar = new StockLocation { Name = "Bar", Code = "BAR", IsActive = true };
            _db.Add(bar);
            await _db.SaveChangesAsync();

            var draft = (await _svc.CreateDraftAsync(new StockCountCreateDto(bar.Id, null))).Data!;
            draft.Lines.Should().OnlyContain(l => l.ExpectedQty == 0m);
            var buns = draft.Lines.Single(l => l.IngredientId == 2);
            await _svc.UpdateLinesAsync(draft.Id, new StockCountLinesUpdateDto(new() { new(buns.Id, 12m) }));

            var posted = await _svc.PostAsync(draft.Id);
            posted.Success.Should().BeTrue(posted.Error);

            (await _db.Set<Ingredient>().FindAsync(2))!.QuantityOnHand.Should().Be(40m);
            (await _db.Set<StockMovement>().CountAsync()).Should().Be(0);
            var ls = await _db.Set<LocationStock>().SingleAsync();
            ls.LocationId.Should().Be(bar.Id);
            ls.IngredientId.Should().Be(2);
            ls.Quantity.Should().Be(12m);
        }

        [Fact]
        public async Task Only_drafts_can_be_posted_and_nothing_counted_is_rejected()
        {
            var draft = (await _svc.CreateDraftAsync(new StockCountCreateDto(null, null))).Data!;
            (await _svc.PostAsync(draft.Id)).Success.Should().BeFalse();

            (await _svc.CancelAsync(draft.Id)).Success.Should().BeTrue();
            (await _svc.PostAsync(draft.Id)).Success.Should().BeFalse();
            (await _svc.CreateDraftAsync(new StockCountCreateDto(null, null))).Success.Should().BeTrue("a cancelled sheet no longer blocks a new one");
        }
    }
}
