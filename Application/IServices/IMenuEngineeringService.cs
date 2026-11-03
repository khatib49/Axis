using Application.DTOs;

namespace Application.IServices
{
    public interface IMenuEngineeringService
    {
        /// <summary>
        /// Recipe costing + menu-engineering matrix for the period [from, to).
        /// </summary>
        /// <param name="categoryIds">Item categories in scope (null = all item categories).</param>
        /// <param name="branchId">Reserved for multi-branch scoping; accepted but not yet applied (sales are venue-wide).</param>
        /// <param name="marginThresholdPct">Items whose margin % is below this get LowMarginFlag (default 60).</param>
        Task<MenuEngineeringReportDto> GetReportAsync(
            DateTime? from, DateTime? to,
            IReadOnlyCollection<int>? categoryIds = null,
            int? branchId = null,
            decimal marginThresholdPct = 60m,
            CancellationToken ct = default);

        /// <summary>Ingredient-by-ingredient cost of one item plus a price suggestion for a target margin. Null when the item does not exist.</summary>
        Task<ItemCostBreakdownDto?> GetItemCostBreakdownAsync(int itemId, decimal targetMarginPct = 70m, CancellationToken ct = default);
    }
}
