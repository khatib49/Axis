using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Xunit;

namespace Tests.MenuEngineering
{
    /// <summary>Items without recipe lines are costed from Item.BuyPrice (or 0) and flagged so the chef can fix them.</summary>
    public class MissingRecipeTests : IDisposable
    {
        private readonly ApplicationDbContext _db = TestDb.New();

        public MissingRecipeTests()
        {
            _db.AddRange(
                TestDb.ItemCategory(1, "Menu"),
                new Ingredient { Id = 1, Name = "Coffee beans", Unit = "kg", BuyPricePerUnit = 20m },
                TestDb.MenuItem(1, "Espresso", 3m),
                TestDb.MenuItem(2, "Coke can", 2m, buyPrice: 0.8m),
                TestDb.MenuItem(3, "Mystery plate", 12m),
                new RecipeLine { Id = 1, ItemId = 1, IngredientId = 1, Quantity = 18m, Unit = "g" }, // 0.018 kg → $0.36
                TestDb.Sale(1, 1, 5, 3m),
                TestDb.Sale(2, 2, 5, 2m),
                TestDb.Sale(3, 3, 5, 12m));
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Items_without_recipe_are_flagged_and_listed()
        {
            var r = await TestDb.Service(_db).GetReportAsync(TestDb.Day, TestDb.Day.AddDays(1));

            var espresso = r.Items.Single(i => i.ItemId == 1);
            espresso.HasRecipe.Should().BeTrue();
            espresso.RecipeCost.Should().Be(0.36m);
            espresso.CostSource.Should().Be("recipe");

            var coke = r.Items.Single(i => i.ItemId == 2);
            coke.HasRecipe.Should().BeFalse();
            coke.RecipeCost.Should().Be(0m);
            coke.UnitCost.Should().Be(0.8m);      // falls back to the buy price
            coke.CostSource.Should().Be("buy");
            coke.MarginPct.Should().Be(60m);

            var mystery = r.Items.Single(i => i.ItemId == 3);
            mystery.HasRecipe.Should().BeFalse();
            mystery.RecipeCost.Should().Be(0m);
            mystery.UnitCost.Should().Be(0m);
            mystery.CostSource.Should().Be("none");
            mystery.MarginPct.Should().Be(100m);
            mystery.TotalCost.Should().Be(0m);
            mystery.Contribution.Should().Be(60m);

            r.MissingRecipes.Should().Equal("Coke can", "Mystery plate");
        }

        [Fact]
        public async Task Breakdown_of_an_item_without_recipe_has_no_lines_but_still_suggests_a_price()
        {
            var b = await TestDb.Service(_db).GetItemCostBreakdownAsync(2, 70m);

            b!.HasRecipe.Should().BeFalse();
            b.Lines.Should().BeEmpty();
            b.UnitCost.Should().Be(0.8m);
            b.CostSource.Should().Be("buy");
            b.SuggestedPrice.Should().Be(2.67m);
            b.SuggestedPriceRounded.Should().Be(2.75m);
        }
    }
}
