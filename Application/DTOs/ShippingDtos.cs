namespace Application.DTOs
{
    // ── Zones (admin) ────────────────────────────────────────────────────
    public record ShippingZoneDto(int Id, string Name, string Cities, decimal Fee, decimal? FreeAbove, string? EstimatedDays, int SortOrder, bool IsActive);
    public record ShippingZoneUpsertDto(string Name, string Cities, decimal Fee, decimal? FreeAbove = null, string? EstimatedDays = null, int SortOrder = 0, bool IsActive = true);

    // ── Shipments ────────────────────────────────────────────────────────
    public record ShipmentEventDto(int Id, string? Code, string? Description, string? Location, string? Comments, DateTime EventAt);

    public record ShipmentDto(
        int Id, int OnlineOrderId, string OrderCode, string Provider, string Environment,
        string? AwbNumber, string Status,
        string? ProductGroup, string? ProductType, string? PaymentType, string? Services,
        decimal CodAmount, string? CodCurrency, decimal WeightKg, int Pieces,
        string? LabelUrl, string? PickupGuid, string? PickupId,
        string? LastTrackingCode, string? LastTrackingText, DateTime? LastTrackingAt, DateTime? LastPolledAt,
        string? Error, string? CreatedBy, DateTime CreatedOn, DateTime? DeliveredOn, DateTime? SettledOn, int? SettlementJournalEntryId,
        string? TrackingUrl,
        List<ShipmentEventDto> Events,
        // Order context for the admin list
        string? CustomerName = null, string? CustomerPhone = null, string? City = null, string? PaymentMode = null,
        decimal OrderTotal = 0m, string? OrderStatus = null);

    /// <summary>Admin list of shipments with a few counters for the header.</summary>
    public record ShipmentListDto(
        int OpenCount, int DeliveredUnsettledCount, decimal CodOutstanding,
        List<ShipmentDto> Shipments);

    /// <summary>Record the cash Aramex remitted for one or more delivered COD shipments.</summary>
    public record CodSettlementRequest(
        List<int> ShipmentIds,
        /// Net cash actually received (after Aramex deducted its fees). Defaults to the COD total when null.
        decimal? NetReceived,
        /// Courier charges deducted (booked to 5400). Defaults to COD total − NetReceived.
        decimal? Fees,
        /// "cash" → 1000 Cash on Hand; "bank" → 1050 Online Payments Clearing.
        string ReceivedInto = "cash",
        string? Reference = null);

    public record CodSettlementResult(bool Success, string? Error, int? JournalEntryId, decimal CodTotal, decimal NetReceived, decimal Fees, int ShipmentsSettled);

    /// <summary>Optional overrides when the till creates the Aramex shipment.</summary>
    public record CreateShipmentRequest(decimal? WeightKg = null, int? Pieces = null, string? Comments = null, bool? PrintLabel = true);

    /// <summary>Book an Aramex courier pickup for the shipments waiting at the shop.</summary>
    public record CreatePickupRequest(DateTime? PickupDate, string? ReadyTime, string? LastPickupTime, string? ClosingTime, string? Reference, List<int>? ShipmentIds);
    public record CreatePickupResult(bool Success, string? Error, string? PickupId, string? PickupGuid, int ShipmentsAttached);

    // ── Settings snapshot for the admin Shipping page ────────────────────
    public record ShippingSettingsDto(
        string Environment, bool SandboxConfigured, bool ProductionConfigured,
        string RateMode, bool DeliveryEnabled, bool CodEnabled, bool ShopEnabled,
        string ProductGroup, string ProductType, string PaymentType, string CodCurrency,
        string? ShipperCompany, string? ShipperPerson, string? ShipperPhone, string? ShipperCell, string? ShipperEmail,
        string? ShipperLine1, string? ShipperLine2, string? ShipperCity, string ShipperCountry,
        decimal DefaultWeightKg, int TrackingPollMinutes,
        string SandboxUrl, string ProductionUrl);

    /// <summary>Result of "Test connection": we run CalculateRate for a small parcel Beirut→Beirut.</summary>
    public record ShippingTestResult(bool Ok, string Message, decimal? RateAmount = null, string? RateCurrency = null);

    /// <summary>Live Aramex rate lookup used by the checkout quote (RateMode = aramex).</summary>
    public record CourierRateDto(bool Ok, decimal Amount, string Currency, string? Error);
}
