using Application.DTOs;

namespace Application.IServices
{
    /// <summary>Delivery & takeaway operations on top of existing F&B invoices (OrderingChannels module).</summary>
    public interface IDeliveryService
    {
        // ── Zones ────────────────────────────────────────────────────────
        Task<List<DeliveryZoneDto>> ListZonesAsync(bool includeInactive, CancellationToken ct = default);
        Task<DeliveryZoneDto?> GetZoneAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<DeliveryZoneDto>> CreateZoneAsync(DeliveryZoneUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<DeliveryZoneDto>> UpdateZoneAsync(int id, DeliveryZoneUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteZoneAsync(int id, CancellationToken ct = default);

        // ── Drivers ──────────────────────────────────────────────────────
        Task<List<DriverDto>> ListDriversAsync(bool includeInactive, CancellationToken ct = default);
        Task<BaseResponse<DriverDto>> CreateDriverAsync(DriverUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<DriverDto>> UpdateDriverAsync(int id, DriverUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteDriverAsync(int id, CancellationToken ct = default);

        // ── Orders ───────────────────────────────────────────────────────
        /// <summary>Till creation: validates zone / min-order, creates the open invoice via CreateCoffeeShopOrder and wraps it.</summary>
        Task<BaseResponse<DeliveryOrderDto>> CreateAsync(DeliveryOrderCreateDto dto, CancellationToken ct = default);
        /// <summary>Same as CreateAsync but stamped with an external source (webhook / kiosk) and an explicit actor.</summary>
        Task<BaseResponse<DeliveryOrderDto>> CreateFromSourceAsync(DeliveryOrderCreateDto dto, string createdBy, string? externalSource, string? externalRef, decimal? feeOverride, CancellationToken ct = default);
        Task<DeliveryOrderDto?> GetAsync(int id, CancellationToken ct = default);
        /// <summary>Active orders grouped by status (+ today's delivered / cancelled).</summary>
        Task<DeliveryBoardDto> GetBoardAsync(CancellationToken ct = default);
        Task<BaseResponse<DeliveryOrderDto>> AssignDriverAsync(int id, int driverId, CancellationToken ct = default);
        Task<BaseResponse<DeliveryOrderDto>> AdvanceAsync(int id, string? toStatus, string? note, CancellationToken ct = default);
        Task<BaseResponse<DeliveryOrderDto>> CancelAsync(int id, string? reason, CancellationToken ct = default);
        /// <summary>Orders assigned to the driver linked to the calling user (empty when the user is not a driver).</summary>
        Task<List<DeliveryOrderDto>> MyOrdersAsync(CancellationToken ct = default);
        Task<DeliveryTodayStatsDto> GetTodayStatsAsync(CancellationToken ct = default);

        // ── Aggregator webhook ───────────────────────────────────────────
        /// <summary>
        /// Validates the shared secret for the source, then creates the order
        /// idempotently on (source, externalRef). Error "Unauthorized" when
        /// the secret is missing/wrong, "NotConfigured" when no secret is set.
        /// </summary>
        Task<BaseResponse<WebhookOrderResult>> IngestWebhookAsync(string source, string? providedSecret, WebhookOrderPayload payload, CancellationToken ct = default);
    }
}
