using Application.Services.MenuEngineering;
using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Xunit;

namespace Tests.MenuEngineering
{
    /// <summary>Recipe cost = Σ qty (converted to the ingredient's stock unit) × BuyPricePerUnit.</summary>
    public class RecipeCostTests : IDisposable
    {
        private readonly ApplicationDbContext _db = TestDb.New();

        public RecipeCostTests()
        {
            _db.AddRange(
                TestDb.ItemCategory(1, "Burgers"),
                TestDb.MenuItem(1, "Classic Burger", 10m),
                new Ingredient { Id = 1, Name = "Beef", Unit = "kg", BuyPricePerUnit = 10m },     // $10 per kg
                new Ingredient { Id = 2, Name = "Bun", Unit = "pcs", BuyPricePerUnit = 0.5m },
                new Ingredient { Id = 3, Name = "Milk", Unit = "l", BuyPricePerUnit = 2m },       // $2 per litre
                new Ingredient { Id = 4, Name = "Secret sauce", Unit = "g", BuyPricePerUnit = null },
                new RecipeLine { Id = 1, ItemId = 1, IngredientId = 1, Quantity = 200m, Unit = "g" },   // 0.2 kg → $2.00
                new RecipeLine { Id = 2, ItemId = 1, IngredientId = 2, Quantity = 1m, Unit = "pcs" },   // $0.50
                new RecipeLine { Id = 3, ItemId = 1, IngredientId = 3, Quantity = 50m, Unit = "ml" },   // 0.05 l → $0.10
                new RecipeLine { Id = 4, ItemId = 1, IngredientId = 4, Quantity = 20m, Unit = null });  // unpriced → $0
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Breakdown_converts_recipe_units_to_ingredient_units_before_pricing()
        {
            var b = await TestDb.Service(_db).GetItemCostBreakdownAsync(1, 70m);

            b.Should().NotBeNull();
            b!.HasRecipe.Should().BeTrue();
            b.CostSource.Should().Be("recipe");
            b.Lines.Should().HaveCount(4);

            var beef = b.Lines.Single(l => l.Ingredient == "Beef");
            beef.Quantity.Should().Be(200m);
            beef.Unit.Should().Be("g");
            beef.ConvertedQuantity.Should().Be(0.2m);
            beef.IngredientUnit.Should().Be("kg");
            beef.LineCost.Should().Be(2.00m);
            beef.Converted.Should().BeTrue();

            b.Lines.Single(l => l.Ingredient == "Milk").LineCost.Should().Be(0.10m);
            b.Lines.Single(l => l.Ingredient == "Secret sauce").LineCost.Should().Be(0m);

            b.RecipeCost.Should().Be(2.60m);
            b.UnitCost.Should().Be(2.60m);
            b.GrossMargin.Should().Be(7.40m);
            b.MarginPct.Should().Be(74.0m);
            b.UnpricedIngredients.Should().ContainSingle().Which.Should().Be("Secret sauce");
        }

        [Fact]
        public async Task Price_suggestion_hits_the_target_margin()
        {
            var b = await TestDb.Service(_db).GetItemCostBreakdownAsync(1, 70m);

            // 2.60 / (1 − 0.70) = 8.666… → 8.67, rounded up to the next quarter → 8.75
            b!.TargetMarginPct.Should().Be(70m);
            b.SuggestedPrice.Should().Be(8.67m);
            b.SuggestedPriceRounded.Should().Be(8.75m);
            ((b.SuggestedPrice - b.UnitCost) / b.SuggestedPrice * 100m).Should().BeApproximately(70m, 0.1m);

            MenuEngineeringService.SuggestPrice(2.60m, 50m).Should().Be(5.20m);
            // Nonsense targets fall back to the 70 % default instead of dividing by zero.
            MenuEngineeringService.SuggestPrice(3m, 100m).Should().Be(10.00m);
            MenuEngineeringService.SuggestPrice(3m, -5m).Should().Be(10.00m);
        }

        [Fact]
        public async Task Report_uses_the_same_recipe_cost_even_when_a_buy_price_exists()
        {
            // A stale Item.BuyPrice must not win over the recipe.
            _db.Set<Item>().Single(i => i.Id == 1).BuyPrice = 99m;
            _db.SaveChanges();

            var report = await TestDb.Service(_db).GetReportAsync(TestDb.Day, TestDb.Day.AddDays(1));

            var burger = report.Items.Should().ContainSingle().Subject;
            burger.HasRecipe.Should().BeTrue();
            burger.RecipeCost.Should().Be(2.60m);
            burger.UnitCost.Should().Be(2.60m);
            burger.CostSource.Should().Be("recipe");
            report.MissingRecipes.Should().BeEmpty();
        }

        [Fact]
        public async Task Unknown_item_returns_null()
        {
            (await TestDb.Service(_db).GetItemCostBreakdownAsync(999)).Should().BeNull();
        }
    }
}
