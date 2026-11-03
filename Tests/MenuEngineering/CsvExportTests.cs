using Application.DTOs;
using Application.Services.MenuEngineering;
using FluentAssertions;
using Xunit;

namespace Tests.MenuEngineering
{
    public class CsvExportTests
    {
        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("", "")]
        [InlineData(null, "")]
        [InlineData("Fish, chips", "\"Fish, chips\"")]
        [InlineData("The \"Big\" One", "\"The \"\"Big\"\" One\"")]
        [InlineData("line1\nline2", "\"line1\nline2\"")]
        public void Fields_are_quoted_and_escaped_per_rfc4180(string? input, string expected) =>
            MenuEngineeringCsv.Escape(input).Should().Be(expected);

        private static MenuEngineeringItemDto Row(string name, string category, string quadrant) => new(
            ItemId: 1, Name: name, CategoryId: 1, Category: category, Price: 10m,
            RecipeCost: 2.5m, HasRecipe: true, UnitCost: 2.5m, CostSource: "recipe",
            GrossMargin: 7.5m, MarginPct: 75m, UnitsSold: 12, Revenue: 118.8m, TotalCost: 30m, Contribution: 88.8m,
            PopularityIndex: 1.2m, ProfitabilityIndex: 1.05m, IsPopular: true, IsProfitable: true,
            Quadrant: quadrant, LowMarginFlag: false);

        [Fact]
        public void Csv_has_header_item_rows_and_a_summary_block()
        {
            var report = new MenuEngineeringReportDto(
                From: null, To: null, GeneratedAt: DateTime.UtcNow, MarginThresholdPct: 60m,
                AvgUnitsPerItem: 10m, PopularityThresholdUnits: 7m, ProfitabilityThresholdPct: 71.4m,
                Items: new[] { Row("Fish, chips", "Mains", "Star"), Row("The \"Big\" One", "Burgers", "Dog") },
                Totals: new MenuEngineeringTotalsDto(237.6m, 60m, 177.6m, 74.7m, 2, 24, 1, 0, 0, 1, 0),
                MissingRecipes: new[] { "Coke, can" });

            var csv = MenuEngineeringCsv.Render(report);
            var lines = csv.Split("\r\n");

            lines[0].Should().Be(string.Join(",", MenuEngineeringCsv.Columns));
            lines[1].Should().StartWith("\"Fish, chips\",Mains,10,2.5,2.5,recipe,7.5,75,12,118.8,30,88.8,1.2,1.05,Star,no,yes");
            lines[2].Should().StartWith("\"The \"\"Big\"\" One\",Burgers,");
            lines[3].Should().BeEmpty();
            lines[4].Should().Be("Total revenue,237.6");
            csv.Should().Contain("Missing recipes,\"Coke, can\"");
            csv.Should().NotContain("\n\n\n"); // no stray blank rows inside the table
        }

        [Fact]
        public void Numbers_are_invariant_culture()
        {
            var prev = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                var report = new MenuEngineeringReportDto(null, null, DateTime.UtcNow, 60m, 1m, 0.7m, 50m,
                    new[] { Row("A", "B", "Star") }, new MenuEngineeringTotalsDto(1.5m, 0.5m, 1m, 66.7m, 1, 1, 1, 0, 0, 0, 0), Array.Empty<string>());
                MenuEngineeringCsv.Render(report).Should().Contain(",2.5,").And.NotContain("2,5");
            }
            finally { Thread.CurrentThread.CurrentCulture = prev; }
        }
    }
}
