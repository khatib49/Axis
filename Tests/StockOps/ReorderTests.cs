using Application.DTOs;
using Application.Services.StockOps;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Tests.StockOps
{
    public class ReorderTests : IDisposable
    {
        private readonly ApplicationDbContext _db;
        private readonly ReorderService _svc;

        public ReorderTests()
        {
            _db = StockOpsTestDb.Create();
            var farms = new Supplier { Id = 1, Name = "Fresh Farms" };
            var dairy = new Supplier { Id = 2, Name = "Beirut Dairy" };
            _db.AddRange(farms, dairy,
                // No rule, below ReorderLevel 10 => suggested 2*10 - 4 = 16
                new Ingredient { Id = 1, Name = "Tomato", Unit = "kg", QuantityOnHand = 4m, ReorderLevel = 10m, BuyPricePerUnit = 1.5m },
                // Rule: min 20 target 50, qty 20 (== min counts) => suggested 30, preferred supplier dairy
                new Ingredient { Id = 2, Name = "Milk", Unit = "l", QuantityOnHand = 20m, ReorderLevel = 5m, BuyPricePerUnit = 0.9m },
                // Above its level => not suggested
                new Ingredient { Id = 3, Name = "Salt", Unit = "kg", QuantityOnHand = 50m, ReorderLevel = 2m, BuyPricePerUnit = 0.2m },
                // No level, no rule => never suggested even at zero
                new Ingredient { Id = 4, Name = "Napkins", Unit = "pcs", QuantityOnHand = 0m },
                new ReorderRule { Id = 1, IngredientId = 2, MinQty = 20m, TargetQty = 50m, PreferredSupplierId = 2, LeadTimeDays = 2, IsActive = true });

            var day = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            _db.AddRange(
                new Purchase { Id = 1, SupplierId = 1, PurchaseDate = day, TotalCost = 10, Lines = new List<PurchaseLine> { new() { Id = 1, IngredientId = 1, Quantity = 10, UnitCost = 1.2m, LineTotal = 12 } } },
                new Purchase { Id = 2, SupplierId = 1, PurchaseDate = day.AddDays(5), TotalCost = 10, Lines = new List<PurchaseLine> { new() { Id = 2, IngredientId = 1, Quantity = 10, UnitCost = 1.8m, LineTotal = 18 } } });
            _db.SaveChanges();
            _svc = new ReorderService(_db);
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Suggestions_use_rule_or_reorder_level_and_latest_purchase_cost()
        {
            var s = await _svc.GetSuggestionsAsync();
            s.Select(x => x.IngredientId).Should().BeEquivalentTo(new[] { 1, 2 });

            var tomato = s.Single(x => x.IngredientId == 1);
            tomato.HasRule.Should().BeFalse();
            tomato.Threshold.Should().Be(10m);
            tomato.SuggestedQty.Should().Be(16m);
            tomato.UnitCost.Should().Be(1.8m, "the latest purchase wins over the older one and BuyPricePerUnit");
            tomato.EstimatedCost.Should().Be(28.80m);
            tomato.SupplierId.Should().Be(1);
            tomato.SupplierName.Should().Be("Fresh Farms");
            tomato.LastPurchaseOn.Should().Be(new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc));

            var milk = s.Single(x => x.IngredientId == 2);
            milk.HasRule.Should().BeTrue();
            milk.Threshold.Should().Be(20m);
            milk.SuggestedQty.Should().Be(30m);
            milk.UnitCost.Should().Be(0.9m, "no purchase yet, so BuyPricePerUnit is used");
            milk.EstimatedCost.Should().Be(27.00m);
            milk.SupplierId.Should().Be(2);
            milk.SupplierName.Should().Be("Beirut Dairy");
            milk.LeadTimeDays.Should().Be(2);
        }

        [Fact]
        public async Task Purchase_draft_matches_the_purchase_create_dto_shape_and_filters_by_supplier()
        {
            var all = await _svc.BuildPurchaseDraftAsync(new ReorderPurchaseDraftRequestDto(null, null));
            all.Success.Should().BeTrue(all.Error);
            all.Data!.SupplierId.Should().BeNull("two different suppliers are involved");
            all.Data.Lines.Should().HaveCount(2);

            var dairyOnly = await _svc.BuildPurchaseDraftAsync(new ReorderPurchaseDraftRequestDto(null, 2));
            dairyOnly.Success.Should().BeTrue(dairyOnly.Error);
            dairyOnly.Data!.SupplierId.Should().Be(2);
            var line = dairyOnly.Data.Lines.Should().ContainSingle().Subject;
            line.IngredientId.Should().Be(2);
            line.Quantity.Should().Be(30m);
            line.UnitCost.Should().Be(0.9m);

            (await _db.Set<Purchase>().CountAsync()).Should().Be(2, "the draft must not create a purchase");
        }

        [Fact]
        public async Task Rules_are_unique_per_ingredient_and_target_must_exceed_min()
        {
            (await _svc.CreateRuleAsync(new ReorderRuleUpsertDto(2, 1m, 5m, null))).Success.Should().BeFalse();
            (await _svc.CreateRuleAsync(new ReorderRuleUpsertDto(1, 5m, 5m, null))).Success.Should().BeFalse();
            var ok = await _svc.CreateRuleAsync(new ReorderRuleUpsertDto(1, 5m, 40m, 1, 3));
            ok.Success.Should().BeTrue(ok.Error);
            ok.Data!.PreferredSupplierName.Should().Be("Fresh Farms");

            var tomato = (await _svc.GetSuggestionsAsync()).Single(x => x.IngredientId == 1);
            tomato.HasRule.Should().BeTrue();
            tomato.SuggestedQty.Should().Be(36m);
        }
    }
}
