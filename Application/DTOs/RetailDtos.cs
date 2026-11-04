namespace Application.DTOs
{
    // ── Barcodes ────────────────────────────────────────────────────────
    public record BarcodeDto(int Id, int ItemId, string ItemName, int? VariantId, string? VariantName,
        string Code, string Symbology, bool IsPrimary, DateTime CreatedOn, string CreatedBy);

    public record BarcodeCreateDto(int ItemId, int? VariantId, string Code, string? Symbology, bool IsPrimary = false);
    public record BarcodeUpdateDto(string Code, string? Symbology, bool IsPrimary);
    public record BarcodeGenerateDto(int ItemId, int? VariantId);

    /// <summary>What the till gets back for a scanned code.</summary>
    public record BarcodeLookupDto(
        int BarcodeId, string Code, string Symbology,
        int ItemId, string ItemName, int? CategoryId, string? ImagePath,
        int? VariantId, string? VariantName,
        decimal Price, int Stock,
        bool RequiresSerial, int SerialsInStock,
        /// <summary>Set when the scanned text was a serial number rather than a barcode.</summary>
        string? MatchedSerial = null);

    public record LabelDto(int ItemId, string ItemName, int? VariantId, string? VariantName,
        string? Code, string? Symbology, decimal Price);

    // ── Serial numbers ──────────────────────────────────────────────────
    public record SerialSettingDto(int ItemId, string ItemName, bool RequiresSerial, string? Prefix, DateTime? ModifiedOn, string? ModifiedBy);
    public record SerialSettingUpsertDto(bool RequiresSerial, string? Prefix);

    public record SerialNumberDto(int Id, int ItemId, string ItemName, int? VariantId, string? VariantName,
        string Serial, string Status, int? TransactionId, DateTime ReceivedOn, DateTime? SoldOn, DateTime? ReturnedOn,
        string? Notes, int? BranchId);

    /// <summary>Either an explicit list of serials or a count to generate with the item prefix.</summary>
    public record SerialReceiveDto(int ItemId, int? VariantId, List<string>? Serials, int? Count, string? Notes);
    public record SerialReceiveResultDto(int Created, List<string> CreatedSerials, List<string> Skipped);

    public record SerialSellDto(string Serial, int TransactionId);
    public record SerialStatusChangeDto(string? Notes);

    public record SerialSummaryDto(int ItemId, string ItemName, bool RequiresSerial, string? Prefix,
        int InStock, int Sold, int Returned, int Faulty, int ItemQuantity, int Variance);
}
