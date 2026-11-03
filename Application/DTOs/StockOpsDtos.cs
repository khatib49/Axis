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
}
