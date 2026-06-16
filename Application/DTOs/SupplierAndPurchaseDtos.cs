namespace Application.DTOs
{
    // ─── Suppliers ────────────────────────────────────────────────────────
    public record SupplierDto(
        int Id,
        string Name,
        string? ContactInfo,
        string? Notes,
        bool IsActive,
        DateTime CreatedOn,
        DateTime? ModifiedOn
    );

    public record SupplierCreateDto(string Name, string? ContactInfo, string? Notes);

    public record SupplierUpdateDto(string Name, string? ContactInfo, string? Notes, bool IsActive);

    // ─── Purchases ────────────────────────────────────────────────────────
    public record PurchaseLineDto(
        int Id,
        int IngredientId,
        string IngredientName,
        string Unit,
        decimal Quantity,
        decimal UnitCost,
        decimal LineTotal,
        string? Notes
    );

    public record PurchaseDto(
        int Id,
        int? SupplierId,
        string? SupplierName,
        DateTime PurchaseDate,
        string? InvoiceNumber,
        decimal TotalCost,
        string? Notes,
        string? CreatedBy,
        DateTime CreatedOn,
        List<PurchaseLineDto> Lines
    );

    public record PurchaseLineInputDto(
        int IngredientId,
        decimal Quantity,
        decimal UnitCost,
        string? Notes
    );

    public record PurchaseCreateDto(
        int? SupplierId,
        DateTime PurchaseDate,
        string? InvoiceNumber,
        string? Notes,
        List<PurchaseLineInputDto> Lines
    );

    public record PurchaseFilterDto(
        int? SupplierId,
        int? IngredientId,
        DateTime? From,
        DateTime? To,
        int Page = 1,
        int PageSize = 25
    );

    // ─── Price trend (per ingredient) ─────────────────────────────────────
    public record PriceTrendPointDto(
        DateTime Date,
        decimal UnitCost,
        decimal Quantity,
        int? SupplierId,
        string? SupplierName,
        int PurchaseId
    );

    // ─── Inventory Valuation ──────────────────────────────────────────────
    public record InventoryValueLineDto(
        int IngredientId,
        string IngredientName,
        string Unit,
        decimal QuantityOnHand,
        decimal? UnitCost,
        decimal Value
    );

    public record InventoryTopMoverDto(
        int IngredientId,
        string IngredientName,
        string Unit,
        decimal ConsumedQuantity,
        decimal ConsumedValue
    );

    public record InventorySlowMoverDto(
        int IngredientId,
        string IngredientName,
        string Unit,
        decimal QuantityOnHand,
        decimal? UnitCost,
        decimal Value,
        DateTime? LastConsumptionOn // null if never consumed
    );

    public record InventoryValuationDto(
        DateTime? From,
        DateTime? To,
        decimal TotalValue,
        int IngredientCount,
        List<InventoryValueLineDto> ByIngredient,
        List<InventoryTopMoverDto> TopMovers,
        List<InventorySlowMoverDto> SlowMovers
    );
}
