namespace Application.DTOs
{
    // ─── Locations ────────────────────────────────────────────────────────
    public record StockLocationDto(int Id, string Name, string Code, bool IsDefault, bool IsActive, int? BranchId);
    public record StockLocationUpsertDto(string Name, string Code, bool IsDefault = false, bool IsActive = true);
    public record LocationStockDto(int IngredientId, string IngredientName, string Unit, decimal Quantity);

    // ─── Stock counts ─────────────────────────────────────────────────────
    public record StockCountSummaryDto(
        int Id,
        int LocationId,
        string LocationName,
        string Reference,
        string Status,
        DateTime CreatedOn,
        string? CreatedBy,
        DateTime? PostedOn,
        string? PostedBy,
        string? Notes,
        int LineCount,
        int CountedLines
    );

    public record StockCountLineDto(
        int Id,
        int IngredientId,
        string IngredientName,
        string Unit,
        decimal ExpectedQty,
        decimal? CountedQty,
        decimal Variance,
        decimal? UnitCost,
        decimal VarianceValue
    );

    public record StockCountDto(
        int Id,
        int LocationId,
        string LocationName,
        string Reference,
        string Status,
        DateTime CreatedOn,
        string? CreatedBy,
        DateTime? PostedOn,
        string? PostedBy,
        string? Notes,
        List<StockCountLineDto> Lines
    );

    public record StockCountCreateDto(int? LocationId, string? Notes, string? Reference = null);
    public record StockCountLineUpdateDto(int LineId, decimal? CountedQty);
    public record StockCountLinesUpdateDto(List<StockCountLineUpdateDto> Lines);

    public record StockCountVarianceReportDto(
        int CountId,
        string Reference,
        string LocationName,
        DateTime? PostedOn,
        int LinesCounted,
        int LinesWithVariance,
        decimal TotalShrinkQtyLines,       // number of lines with negative variance
        decimal TotalShrinkValue,          // sum of negative VarianceValue, as a positive number
        decimal TotalSurplusValue,         // sum of positive VarianceValue
        decimal NetVarianceValue,          // surplus - shrink
        List<StockCountLineDto> TopVariances
    );

    // ─── Transfers ────────────────────────────────────────────────────────
    public record StockTransferLineDto(int Id, int IngredientId, string IngredientName, string Unit, decimal Quantity);
    public record StockTransferLineInputDto(int IngredientId, decimal Quantity);

    public record StockTransferDto(
        int Id,
        int FromLocationId,
        string FromLocationName,
        int ToLocationId,
        string ToLocationName,
        string Reference,
        string Status,
        DateTime CreatedOn,
        string? CreatedBy,
        DateTime? PostedOn,
        string? Notes,
        List<StockTransferLineDto> Lines
    );

    public record StockTransferCreateDto(int FromLocationId, int ToLocationId, string? Notes, List<StockTransferLineInputDto> Lines);
    public record StockTransferUpdateDto(int? FromLocationId, int? ToLocationId, string? Notes, List<StockTransferLineInputDto>? Lines);

    // ─── Reorder ──────────────────────────────────────────────────────────
    public record ReorderRuleDto(
        int Id,
        int IngredientId,
        string IngredientName,
        string Unit,
        decimal MinQty,
        decimal TargetQty,
        int? PreferredSupplierId,
        string? PreferredSupplierName,
        int LeadTimeDays,
        bool IsActive
    );

    public record ReorderRuleUpsertDto(
        int IngredientId,
        decimal MinQty,
        decimal TargetQty,
        int? PreferredSupplierId,
        int LeadTimeDays = 0,
        bool IsActive = true
    );

    public record ReorderSuggestionDto(
        int IngredientId,
        string IngredientName,
        string Unit,
        decimal QuantityOnHand,
        decimal Threshold,
        decimal SuggestedQty,
        decimal? UnitCost,
        decimal EstimatedCost,
        int? SupplierId,
        string? SupplierName,
        DateTime? LastPurchaseOn,
        decimal? LastPurchaseUnitCost,
        int LeadTimeDays,
        bool HasRule
    );

    // Which suggestions to turn into a purchase draft. Empty/null = all of them.
    public record ReorderPurchaseDraftRequestDto(List<int>? IngredientIds, int? SupplierId, string? Notes = null);

    // ─── Batches ──────────────────────────────────────────────────────────
    public record StockBatchDto(
        int Id,
        int IngredientId,
        string IngredientName,
        string Unit,
        int? PurchaseLineId,
        string? LotNumber,
        decimal Quantity,
        decimal RemainingQuantity,
        DateTime? ExpiresOn,
        DateTime ReceivedOn,
        string? Notes,
        int? DaysToExpiry,
        bool IsExpired,
        DateTime? WrittenOffOn
    );

    public record StockBatchCreateDto(
        int IngredientId,
        decimal Quantity,
        DateTime? ExpiresOn,
        int? PurchaseLineId = null,
        string? LotNumber = null,
        DateTime? ReceivedOn = null,
        string? Notes = null
    );

    public record StockBatchUpdateDto(
        string? LotNumber,
        decimal? Quantity,
        decimal? RemainingQuantity,
        DateTime? ExpiresOn,
        DateTime? ReceivedOn,
        string? Notes
    );

    public record StockBatchFilterDto(int? IngredientId = null, bool? OnlyWithRemaining = null, int Page = 1, int PageSize = 25);

    // ─── Branch stock positions (phase 3) ─────────────────────────────────
    /// <summary>
    /// One ingredient at one branch. DefaultQty = quantity at the branch's default
    /// location, OtherQty = sum over the branch's other locations, TotalQty = both.
    /// BelowLevel = TotalQty &lt;= ReorderLevel (rule MinQty, else Ingredient.ReorderLevel),
    /// same convention as the reorder suggestions. Value = TotalQty x BuyPricePerUnit.
    /// </summary>
    public record BranchStockLineDto(
        int IngredientId,
        string IngredientName,
        string Unit,
        decimal DefaultQty,
        decimal OtherQty,
        decimal TotalQty,
        decimal? ReorderLevel,
        bool BelowLevel,
        decimal? UnitCost,
        decimal Value
    );

    /// <summary>BranchId null = the shared scope (global "Main" + shared locations).</summary>
    public record BranchStockPositionDto(
        int? BranchId,
        string BranchCode,
        string BranchName,
        int DefaultLocationId,
        string DefaultLocationName,
        List<StockLocationDto> OtherLocations,
        int BelowLevelCount,
        decimal TotalValue,
        List<BranchStockLineDto> Lines
    );

    public record StockPositionColumnDto(int? BranchId, string Code, string Name, int DefaultLocationId, string DefaultLocationName, decimal TotalValue, int BelowLevelCount);
    public record StockPositionCellDto(int? BranchId, decimal Qty, bool BelowLevel, decimal Value);
    public record StockPositionRowDto(
        int IngredientId,
        string IngredientName,
        string Unit,
        decimal? ReorderLevel,
        decimal? UnitCost,
        List<StockPositionCellDto> Cells,
        decimal TotalQty,
        decimal TotalValue
    );
    /// <summary>All branches side by side; Columns[0] is always the shared scope (BranchId null).</summary>
    public record StockPositionsDto(List<StockPositionColumnDto> Columns, List<StockPositionRowDto> Rows, decimal TotalValue);

    public record ReceiveToLocationDto(int PurchaseId, int LocationId, string? Notes = null);
    /// <summary>AlreadyReceived = the purchase had been received before; Transfer is the existing one.</summary>
    public record ReceiveToLocationResultDto(int PurchaseId, int LocationId, string LocationName, bool AlreadyReceived, StockTransferDto Transfer);
}
