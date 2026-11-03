using Domain.Entities;
using FluentAssertions;
using Infrastructure.Persistence;
using Xunit;

namespace Tests.MenuEngineering
{
    /// <summary>
    /// Four items, all priced $10. Two sell well (10 units) and two barely (1 unit);
    /// two have an 80 % margin and two a 40 % margin, so every quadrant has one item.
    /// </summary>
    public class QuadrantClassificationTests : IDisposable
    {
        private readonly ApplicationDbContext _db = TestDb.New();

        public QuadrantClassificationTests()
        {
            _db.AddRange(
                TestDb.ItemCategory(1, "Mains"),
                TestDb.ItemCategory(2, "Drinks"),
                new Ingredient { Id = 1, Name = "Cheap", Unit = "pcs", BuyPricePerUnit = 2m },
                new Ingredient { Id = 2, Name = "Dear", Unit = "pcs", BuyPricePerUnit = 6m },
                TestDb.MenuItem(1, "Star burger", 10m),        // cost 2, sold 10
                TestDb.MenuItem(2, "Plowhorse pasta", 10m),    // cost 6, sold 10
                TestDb.MenuItem(3, "Puzzle steak", 10m),       // cost 2, sold 1
                TestDb.MenuItem(4, "Dog salad", 10m),          // cost 6, sold 1
                TestDb.MenuItem(5, "Soda", 3m, categoryId: 2, buyPrice: 1m), // other category, used by the filter test
                new RecipeLine { Id = 1, ItemId = 1, IngredientId = 1, Quantity = 1m, Unit = "pcs" },
                new RecipeLine { Id = 2, ItemId = 2, IngredientId = 2, Quantity = 1m, Unit = "pcs" },
                new RecipeLine { Id = 3, ItemId = 3, IngredientId = 1, Quantity = 1m, Unit = "pcs" },
                new RecipeLine { Id = 4, ItemId = 4, IngredientId = 2, Quantity = 1m, Unit = "pcs" },
                TestDb.Sale(1, 1, 10, 10m),
                TestDb.Sale(2, 2, 10, 10m),
                TestDb.Sale(3, 3, 1, 10m),
                TestDb.Sale(4, 4, 1, 10m),
                TestDb.Sale(5, 5, 4, 3m),
                // Outside the period and an unpaid tab: both must be ignored.
                TestDb.Sale(6, 4, 50, 10m, TestDb.Day.AddDays(-3)),
                new TransactionRecord { Id = 7, StatusId = 7, TotalPrice = 100, CreatedOn = TestDb.Day.AddHours(1), CreatedBy = "t",
                    TransactionItems = new List<TransactionItem> { new() { TransactionRecordId = 7, ItemId = 4, Quantity = 10 } } });
            _db.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Each_item_lands_in_its_quadrant()
        {
            var r = await TestDb.Service(_db).GetReportAsync(TestDb.Day, TestDb.Day.AddDays(1), categoryIds: new[] { 1 });

            r.Items.Should().HaveCount(4);
            r.Totals.UnitsSold.Should().Be(22);
            r.AvgUnitsPerItem.Should().Be(5.5m);
            r.PopularityThresholdUnits.Should().Be(3.85m);
            // units-weighted margin: (8×10 + 4×10 + 8 + 4) / 220 = 60 %
            r.ProfitabilityThresholdPct.Should().Be(60m);

            r.Items.Single(i => i.ItemId == 1).Quadrant.Should().Be("Star");
            r.Items.Single(i => i.ItemId == 2).Quadrant.Should().Be("Plowhorse");
            r.Items.Single(i => i.ItemId == 3).Quadrant.Should().Be("Puzzle");
            r.Items.Single(i => i.ItemId == 4).Quadrant.Should().Be("Dog");

            r.Totals.Stars.Should().Be(1);
            r.Totals.Plowhorses.Should().Be(1);
            r.Totals.Puzzles.Should().Be(1);
            r.Totals.Dogs.Should().Be(1);
        }

        [Fact]
        public async Task Indices_margins_and_totals_add_up()
        {
            var r = await TestDb.Service(_db).GetReportAsync(TestDb.Day, TestDb.Day.AddDays(1), categoryIds: new[] { 1 });

            var star = r.Items.Single(i => i.ItemId == 1);
            star.Price.Should().Be(10m);
            star.UnitCost.Should().Be(2m);
            star.GrossMargin.Should().Be(8m);
            star.MarginPct.Should().Be(80m);
            star.UnitsSold.Should().Be(10);
            star.Revenue.Should().Be(100m);
            star.TotalCost.Should().Be(20m);
            star.Contribution.Should().Be(80m);
            star.PopularityIndex.Should().Be(1.82m);      // 10 / 5.5
            star.ProfitabilityIndex.Should().Be(1.33m);   // 80 / 60
            star.IsPopular.Should().BeTrue();
            star.IsProfitable.Should().BeTrue();

            var dog = r.Items.Single(i => i.ItemId == 4);
            dog.PopularityIndex.Should().Be(0.18m);
            dog.ProfitabilityIndex.Should().Be(0.67m);
            dog.Contribution.Should().Be(4m);

            r.Totals.Revenue.Should().Be(220m);
            r.Totals.Cost.Should().Be(20m + 60m + 2m + 6m);
            r.Totals.Contribution.Should().Be(132m);
            r.Totals.AvgMarginPct.Should().Be(60m);
            r.Totals.ItemCount.Should().Be(4);
            r.Items.Should().BeInDescendingOrder(i => i.Contribution);
        }

        [Fact]
        public async Task Low_margin_flag_follows_the_configurable_threshold()
        {
            var svc = TestDb.Service(_db);

            var def = await svc.GetReportAsync(TestDb.Day, TestDb.Day.AddDays(1), categoryIds: new[] { 1 });
            def.MarginThresholdPct.Should().Be(60m);
            def.Items.Where(i => i.LowMarginFlag).Select(i => i.ItemId).Should().BeEquivalentTo(new[] { 2, 4 }); // 40 % < 60 %
            def.Totals.LowMarginCount.Should().Be(2);

            var strict = await svc.GetReportAsync(TestDb.Day, TestDb.Day.AddDays(1), categoryIds: new[] { 1 }, marginThresholdPct: 85m);
            strict.Items.Should().OnlyContain(i => i.LowMarginFlag);

            var lax = await svc.GetReportAsync(TestDb.Day, TestDb.Day.AddDays(1), categoryIds: new[] { 1 }, marginThresholdPct: 30m);
            lax.Items.Should().OnlyContain(i => !i.LowMarginFlag);

            // Out-of-range threshold → default.
            (await svc.GetReportAsync(TestDb.Day, TestDb.Day.AddDays(1), marginThresholdPct: 250m)).MarginThresholdPct.Should().Be(60m);
        }

        [Fact]
        public async Task Category_filter_scopes_the_menu_and_its_averages()
        {
            var all = await TestDb.Service(_db).GetReportAsync(TestDb.Day, TestDb.Day.AddDays(1));
            all.Items.Should().HaveCount(5);
            all.Totals.UnitsSold.Should().Be(26);
            all.AvgUnitsPerItem.Should().Be(5.2m);

            var drinks = await TestDb.Service(_db).GetReportAsync(TestDb.Day, TestDb.Day.AddDays(1), categoryIds: new[] { 2 });
            var soda = drinks.Items.Should().ContainSingle().Subject;
            soda.Category.Should().Be("Drinks");
            soda.UnitsSold.Should().Be(4);
            soda.CostSource.Should().Be("buy");
            // Alone on its menu it is both the average and above it: popular, and profitable at 66.7 % ≥ 66.7 %.
            soda.Quadrant.Should().Be("Star");
        }

        [Fact]
        public async Task Empty_period_still_lists_every_item_as_unpopular()
        {
            var r = await TestDb.Service(_db).GetReportAsync(TestDb.Day.AddDays(30), TestDb.Day.AddDays(31), categoryIds: new[] { 1 });

            r.Items.Should().HaveCount(4);
            r.Totals.UnitsSold.Should().Be(0);
            r.Totals.Revenue.Should().Be(0);
            r.AvgUnitsPerItem.Should().Be(0);
            r.Items.Should().OnlyContain(i => !i.IsPopular && i.PopularityIndex == 0);
            // With nothing sold the profitability line falls back to the plain average margin % (60 %),
            // so the high-margin items are Puzzles and the low-margin ones Dogs.
            r.ProfitabilityThresholdPct.Should().Be(60m);
            r.Items.Where(i => i.Quadrant == "Puzzle").Select(i => i.ItemId).Should().BeEquivalentTo(new[] { 1, 3 });
            r.Items.Where(i => i.Quadrant == "Dog").Select(i => i.ItemId).Should().BeEquivalentTo(new[] { 2, 4 });
        }
    }
}
