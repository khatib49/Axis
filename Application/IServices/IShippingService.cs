using Application.DTOs;

namespace Application.IServices
{
    /// <summary>
    /// Delivery for website orders: fee quotes (zones or live Aramex rate),
    /// Aramex shipments + labels + pickups, tracking, and the cash-on-delivery
    /// accounting (1060 receivable → settlement).
    /// </summary>
    public interface IShippingService
    {
        // ── Settings / zones ────────────────────────────────────────────
        Task<ShippingSettingsDto> GetSettingsAsync(CancellationToken ct = default);
        Task<ShippingTestResult> TestConnectionAsync(CancellationToken ct = default);
        Task<List<ShippingZoneDto>> ListZonesAsync(bool includeInactive, CancellationToken ct = default);
        Task<ShippingZoneDto> UpsertZoneAsync(int? id, ShippingZoneUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteZoneAsync(int id, CancellationToken ct = default);

        // ── Checkout helpers (used by ShopService) ──────────────────────
        Task<bool> IsDeliveryEnabledAsync(CancellationToken ct = default);
        Task<bool> IsCodEnabledAsync(CancellationToken ct = default);
        Task<decimal> DefaultWeightKgAsync(CancellationToken ct = default);
        /// <summary>Fee for a parcel to <paramref name="city"/>: zone table, live Aramex rate (with zone fallback) or free — per Shop.RateMode.</summary>
        Task<ShopQuoteDto> QuoteAsync(decimal subtotal, decimal weightKg, int pieces, string city, decimal? codAmount, CancellationToken ct = default);

        // ── Shipments ───────────────────────────────────────────────────
        Task<(bool ok, string? error, ShipmentDto? shipment)> CreateShipmentForOrderAsync(int orderId, CreateShipmentRequest req, string actor, CancellationToken ct = default);
        Task<(bool ok, string? error, ShipmentDto? shipment)> PrintLabelAsync(int shipmentId, CancellationToken ct = default);
        Task<(bool ok, string? error, ShipmentDto? shipment)> RefreshTrackingAsync(int shipmentId, CancellationToken ct = default);
        Task<(bool ok, string? error, ShipmentDto? shipment)> MarkDeliveredAsync(int shipmentId, string actor, CancellationToken ct = default);
        Task<(bool ok, string? error, ShipmentDto? shipment)> MarkFailedAsync(int shipmentId, string status, string? reason, string actor, CancellationToken ct = default);
        Task<ShipmentDto?> GetShipmentAsync(int shipmentId, CancellationToken ct = default);
        Task<ShipmentDto?> GetShipmentForOrderAsync(int orderId, CancellationToken ct = default);
        Task<ShipmentListDto> ListShipmentsAsync(string? status, DateTime? from, DateTime? to, string? search, CancellationToken ct = default);
        Task<CreatePickupResult> CreatePickupAsync(CreatePickupRequest req, string actor, CancellationToken ct = default);

        /// <summary>Hangfire: poll Aramex for every open shipment and apply status changes.</summary>
        Task<int> PollOpenShipmentsAsync(CancellationToken ct = default);

        // ── Accounting ──────────────────────────────────────────────────
        Task<CodSettlementResult> SettleCodAsync(CodSettlementRequest req, string actor, CancellationToken ct = default);
        /// <summary>Book the customer's delivery fee to 4400 (idempotent per order). Called when the order's money is real.</summary>
        Task BookDeliveryFeeAsync(int orderId, CancellationToken ct = default);
    }
}
