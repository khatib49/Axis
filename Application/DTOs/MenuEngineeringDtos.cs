namespace Application.DTOs
{
    // One menu item on the menu-engineering matrix.
    public record MenuEngineeringItemDto(
        int ItemId,
        string Name,
        int CategoryId,
        string Category,
        decimal Price,
        // Σ recipe line qty (converted to the ingredient unit) × Ingredient.BuyPricePerUnit. 0 when the item has no recipe.
        decimal RecipeCost,
        bool HasRecipe,
        // Cost actually used for the margin: RecipeCost when there is a recipe, else Item.BuyPrice, else 0.
        decimal UnitCost,
        // "recipe" | "buy" | "none"
        string CostSource,
        decimal GrossMargin,
        decimal MarginPct,
        int UnitsSold,
        decimal Revenue,
        decimal TotalCost,
        decimal Contribution,
        decimal PopularityIndex,
        decimal ProfitabilityIndex,
        bool IsPopular,
        bool IsProfitable,
        // Star | Plowhorse | Puzzle | Dog
        string Quadrant,
        bool LowMarginFlag);

    public record MenuEngineeringTotalsDto(
        decimal Revenue,
        decimal Cost,
        decimal Contribution,
        // Realised margin over the period: Contribution / Revenue × 100.
        decimal AvgMarginPct,
        int ItemCount,
        int UnitsSold,
        int Stars,
        int Plowhorses,
        int Puzzles,
        int Dogs,
        int LowMarginCount);

    public record MenuEngineeringReportDto(
        DateTime? From,
        DateTime? To,
        DateTime GeneratedAt,
        decimal MarginThresholdPct,
        // Average units sold per item in scope; an item is popular when units ≥ 70 % of this.
        decimal AvgUnitsPerItem,
        // Units needed to count as popular (0.7 × AvgUnitsPerItem).
        decimal PopularityThresholdUnits,
        // Units-weighted average margin % of the menu; an item is profitable when its margin % is at least this.
        decimal ProfitabilityThresholdPct,
        IReadOnlyList<MenuEngineeringItemDto> Items,
        MenuEngineeringTotalsDto Totals,
        IReadOnlyList<string> MissingRecipes);

    public record ItemCostBreakdownLineDto(
        int IngredientId,
        string Ingredient,
        decimal Quantity,
        string Unit,
        // Quantity expressed in the ingredient's stock unit (what the price is per).
        decimal ConvertedQuantity,
        string IngredientUnit,
        // Ingredient.BuyPricePerUnit; null when the ingredient has no price yet.
        decimal? UnitCost,
        decimal LineCost,
        // False when the recipe unit could not be converted to the ingredient unit (qty used as-is).
        bool Converted);

    public record ItemCostBreakdownDto(
        int ItemId,
        string Name,
        string Category,
        decimal Price,
        bool HasRecipe,
        decimal RecipeCost,
        decimal UnitCost,
        string CostSource,
        decimal GrossMargin,
        decimal MarginPct,
        IReadOnlyList<ItemCostBreakdownLineDto> Lines,
        decimal TargetMarginPct,
        // Price that yields TargetMarginPct on UnitCost: cost / (1 − target/100).
        decimal SuggestedPrice,
        // SuggestedPrice rounded up to the next 0.25.
        decimal SuggestedPriceRounded,
        // Ingredients that have no BuyPricePerUnit — the cost is understated until they are priced.
        IReadOnlyList<string> UnpricedIngredients);
}
