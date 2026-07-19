namespace Application.DTOs
{
    // ─── Ingredients ──────────────────────────────────────────────────────
    public record IngredientDto(
        int Id,
        string Name,
        string Unit,
        decimal QuantityOnHand,
        decimal? ReorderLevel,
        decimal? BuyPricePerUnit,
        bool IsActive,
        string? Notes,
        DateTime CreatedOn,
        DateTime? ModifiedOn,
        // Convenience flags so the UI doesn't have to do the math itself.
        bool IsBelowReorderLevel,
        bool IsNegative
    );

    public record IngredientCreateDto(
        string Name,
        string Unit,
        decimal? ReorderLevel,
        decimal? BuyPricePerUnit,
        string? Notes,
        decimal? OpeningQuantity   // optional initial stock, recorded as a Purchase movement
    );

    public record IngredientUpdateDto(
        string Name,
        string Unit,
        decimal? ReorderLevel,
        decimal? BuyPricePerUnit,
        string? Notes,
        bool IsActive
    );

    // ─── Stock movements (manual events) ──────────────────────────────────
    public record AddStockRequestDto(
        int IngredientId,
        decimal Quantity,
        string? Notes,
        // Optional unit cost. When provided we (a) snapshot it on the
        // StockMovement and (b) update Ingredient.BuyPricePerUnit so the
        // next sale uses the latest cost. If omitted, no cost is captured
        // and the ingredient's existing cost stays.
        decimal? UnitCost = null
    );

    public record RecordWasteRequestDto(
        int IngredientId,
        decimal Quantity,            // positive — the service flips the sign
        string WasteReason,          // 'Spoilage', 'Spillage', 'Burnt', 'Expired', 'Customer Return', 'Other'
        string? Notes
    );

    public record AdjustStockRequestDto(
        int IngredientId,
        decimal NewQuantity,         // absolute target. The service computes the delta and records it as an Adjustment.
        string? Notes
    );

    public record StockMovementDto(
        int Id,
        int IngredientId,
        string IngredientName,
        string IngredientUnit,
        decimal Quantity,
        string Type,
        string? ReferenceType,
        int? ReferenceId,
        string? WasteReason,
        string? Notes,
        decimal BalanceAfter,
        string? CreatedBy,
        DateTime CreatedOn,
        decimal? UnitCost = null,
        decimal? TotalCost = null
    );

    public record StockMovementFilterDto(
        int? IngredientId,
        string? Type,
        DateTime? From,
        DateTime? To,
        int Page = 1,
        int PageSize = 50
    );

    // ─── Recipes ──────────────────────────────────────────────────────────
    public record RecipeLineDto(
        int Id,
        int ItemId,
        int IngredientId,
        string IngredientName,
        // Ingredient's canonical unit — kept for backward compatibility.
        // FE uses it to show the "stored as: kg" hint next to the picker.
        string Unit,
        decimal Quantity,
        string? Notes,
        // Unit the recipe line's Quantity is expressed in. May be null on
        // legacy rows (pre-Bug#9), in which case it falls back to Unit.
        // Backend converts to Unit before touching stock.
        string? RecipeUnit = null
    );

    public record RecipeLineUpsertDto(
        int IngredientId,
        decimal Quantity,
        string? Notes,
        // Optional per-line unit; null = "same as ingredient's unit".
        string? Unit = null
    );

    public record RecipeUpsertRequestDto(
        int ItemId,
        List<RecipeLineUpsertDto> Lines  // full replacement: server diffs and applies add/update/remove
    );

    // ─── Consume-on-sale result ──────────────────────────────────────────
    // Returned to the caller after a sale so the cashier UI can show a
    // yellow toast naming which ingredients went negative.
    public record StockConsumptionWarningDto(
        int IngredientId,
        string IngredientName,
        string Unit,
        decimal QuantityAfter
    );

    // ─── One-shot rebuild of historical Consumption costs (Bug#10) ───────
    public record RebuildConsumptionCostsFilterDto(
        DateTime? From = null,
        DateTime? To = null,
        // When true, no writes happen. Use this to preview the impact
        // before flipping DryRun=false and applying.
        bool DryRun = true,
        // Cap the details list returned so the payload stays reasonable.
        int DetailLimit = 200
    );

    public record RebuildLineDto(
        int MovementId,
        int TransactionId,
        int IngredientId,
        string IngredientName,
        decimal OldQuantity,
        decimal NewQuantity,
        decimal? OldUnitCost,
        decimal? NewUnitCost,
        decimal? OldTotalCost,
        decimal? NewTotalCost,
        // Explains why this row was rebuilt (recipe change, ingredient
        // gone, missing cost, etc.). Purely for the summary UI.
        string Reason
    );

    public record RebuildConsumptionCostsResultDto(
        bool DryRun,
        DateTime? From,
        DateTime? To,
        int MovementsScanned,
        int MovementsChanged,
        int TransactionsAffected,
        decimal OldTotalCogs,
        decimal NewTotalCogs,
        decimal Delta,
        // Per-ingredient QoH adjustments applied (or that WOULD apply on
        // a non-dry run). Positive = ingredient stock was over-consumed
        // historically and got restored; negative = the opposite.
        Dictionary<string, decimal> QoHAdjustments,
        IReadOnlyList<RebuildLineDto> Details
    );
}
