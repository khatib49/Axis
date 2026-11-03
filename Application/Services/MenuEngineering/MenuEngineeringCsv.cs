using System.Globalization;
using System.Text;
using Application.DTOs;

namespace Application.Services.MenuEngineering
{
    /// <summary>RFC 4180 rendering of the menu-engineering report. Static so it is trivially testable.</summary>
    public static class MenuEngineeringCsv
    {
        public static readonly string[] Columns =
        {
            "Item", "Category", "Price", "Recipe cost", "Unit cost", "Cost source", "Gross margin", "Margin %",
            "Units sold", "Revenue", "Total cost", "Contribution", "Popularity index", "Profitability index",
            "Quadrant", "Low margin", "Has recipe",
        };

        public static string Escape(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var mustQuote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            return mustQuote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private static string N(decimal v) => v.ToString(CultureInfo.InvariantCulture);

        public static string Render(MenuEngineeringReportDto report)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", Columns.Select(Escape))).Append("\r\n");
            foreach (var i in report.Items)
            {
                var cells = new[]
                {
                    i.Name, i.Category, N(i.Price), N(i.RecipeCost), N(i.UnitCost), i.CostSource, N(i.GrossMargin), N(i.MarginPct),
                    i.UnitsSold.ToString(CultureInfo.InvariantCulture), N(i.Revenue), N(i.TotalCost), N(i.Contribution),
                    N(i.PopularityIndex), N(i.ProfitabilityIndex), i.Quadrant, i.LowMarginFlag ? "yes" : "no", i.HasRecipe ? "yes" : "no",
                };
                sb.Append(string.Join(",", cells.Select(Escape))).Append("\r\n");
            }
            sb.Append("\r\n");
            sb.Append("Total revenue,").Append(N(report.Totals.Revenue)).Append("\r\n");
            sb.Append("Total cost,").Append(N(report.Totals.Cost)).Append("\r\n");
            sb.Append("Total contribution,").Append(N(report.Totals.Contribution)).Append("\r\n");
            sb.Append("Average margin %,").Append(N(report.Totals.AvgMarginPct)).Append("\r\n");
            sb.Append("Popularity threshold (units),").Append(N(report.PopularityThresholdUnits)).Append("\r\n");
            sb.Append("Profitability threshold (margin %),").Append(N(report.ProfitabilityThresholdPct)).Append("\r\n");
            sb.Append("Low-margin threshold %,").Append(N(report.MarginThresholdPct)).Append("\r\n");
            if (report.MissingRecipes.Count > 0)
                sb.Append("Missing recipes,").Append(Escape(string.Join("; ", report.MissingRecipes))).Append("\r\n");
            return sb.ToString();
        }
    }
}
