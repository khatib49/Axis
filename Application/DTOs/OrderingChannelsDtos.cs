namespace Application.DTOs
{
    // ── Delivery zones / drivers ─────────────────────────────────────────
    public record DeliveryZoneDto(int Id, string Name, decimal Fee, decimal MinOrder, int EtaMinutes, bool IsActive, int? BranchId);
    public record DeliveryZoneUpsertDto(string Name, decimal Fee, decimal MinOrder, int EtaMinutes = 30, bool IsActive = true);

    public record DriverDto(int Id, string Name, string? Phone, string? VehicleNote, int? UserId, bool IsActive, int? BranchId);
    public record DriverUpsertDto(string Name, string? Phone, string? VehicleNote, int? UserId, bool IsActive = true);

    // ── Delivery / takeaway orders ───────────────────────────────────────
    public record DeliveryOrderLineDto(int ItemId, string ItemName, int Quantity, decimal UnitPrice, decimal LineTotal);

    public record DeliveryStatusEventDto(int Id, string? FromStatus, string ToStatus, DateTime On, string By, string? Note);

    public record DeliveryOrderDto(
        int Id,
        int TransactionId,
        string Type,
        int? ZoneId,
        string? ZoneName,
        int? DriverId,
        string? DriverName,
        string CustomerName,
        string? Phone,
        string? Address,
        string? Notes,
        decimal DeliveryFee,
        /// <summary>Invoice total (items, discount applied) — the fee is NOT part of it.</summary>
        decimal Subtotal,
        /// <summary>Subtotal + DeliveryFee.</summary>
        decimal Total,
        string Status,
        DateTime? ScheduledFor,
        DateTime CreatedOn,
        string CreatedBy,
        DateTime? AssignedOn,
        DateTime? PickedUpOn,
        DateTime? DeliveredOn,
        string? CancelledReason,
        string? ExternalSource,
        string? ExternalRef,
        int? ChannelId,
        string? ChannelName,
        bool InvoicePaid,
        List<DeliveryOrderLineDto> Items,
        List<DeliveryStatusEventDto> Events);

    public record DeliveryOrderCreateDto(
        /// <summary>Delivery | Takeaway</summary>
        string Type,
        string CustomerName,
        string? Phone,
        string? Address,
        string? Notes,
        List<OrderItemRequest> Items,
        int? ZoneId = null,
        int? ChannelId = null,
        int DiscountId = 0,
        DateTime? ScheduledFor = null,
        int? UserId = null);

    public record DeliveryAssignDriverDto(int DriverId);
    /// <summary>ToStatus null = move to the next natural status.</summary>
    public record DeliveryAdvanceDto(string? ToStatus = null, string? Note = null);
    public record DeliveryCancelDto(string? Reason);

    public record DeliveryBoardColumnDto(string Status, List<DeliveryOrderDto> Orders);
    public record DeliveryBoardDto(List<DeliveryBoardColumnDto> Columns, DateTime GeneratedOn);

    public record DeliveryTodayStatsDto(
        DateTime Day,
        int Total,
        int Pending,
        int Preparing,
        int Ready,
        int OutForDelivery,
        int Delivered,
        int Cancelled,
        int DeliveryCount,
        int TakeawayCount,
        decimal Revenue,
        decimal Fees,
        double? AvgMinutesToDelivered);

    // ── Aggregator webhook ───────────────────────────────────────────────
    public record WebhookOrderItem(int? ItemId, string? Sku, int Quantity);
    public record WebhookOrderPayload(
        string ExternalRef,
        string CustomerName,
        string? Phone,
        string? Address,
        List<WebhookOrderItem> Items,
        string? Note = null,
        decimal Fee = 0,
        /// <summary>Delivery (default) | Takeaway</summary>
        string? Type = null);
    public record WebhookOrderResult(bool Duplicate, DeliveryOrderDto Order);

    // ── Self-ordering (QR table) ─────────────────────────────────────────
    public record SelfOrderTableDto(int Id, string Code, string Label, bool IsActive, int? BranchId);
    public record SelfOrderTableUpsertDto(string Code, string Label, bool IsActive = true);
    /// <summary>What the admin prints: the public URL and the exact string to encode in the QR.</summary>
    public record SelfOrderTableQrDto(int Id, string Code, string Label, bool IsActive, string PublicUrl, string QrPayload);

    public record SelfOrderMenuCategoryDto(int Id, string Name, List<ItemMenuDto> Items);
    public record SelfOrderMenuDto(string TableCode, string TableLabel, List<SelfOrderMenuCategoryDto> Categories);

    public record SelfOrderSessionStartedDto(Guid Token, string TableCode, string TableLabel, string Status, DateTime CreatedOn);
    public record SelfOrderSubmitDto(List<OrderItemRequest> Items, string? Note = null);
    public record SelfOrderSessionStatusDto(
        Guid Token,
        string TableCode,
        string Status,
        int? TransactionId,
        bool InvoicePaid,
        decimal Total,
        List<DeliveryOrderLineDto> Items,
        string? GuestNote,
        DateTime CreatedOn,
        DateTime LastActivityOn);

    // ── Kiosk ────────────────────────────────────────────────────────────
    public record KioskDeviceDto(int Id, string Name, string Token, bool IsActive, DateTime? LastSeenOn, int? BranchId);
    public record KioskDeviceUpsertDto(string Name, bool IsActive = true);
    public record KioskOrderRequest(List<OrderItemRequest> Items, string? CustomerName = null, string? Note = null);
    public record KioskOrderResultDto(int OrderNumber, int TransactionId, decimal Total, string DeviceName, DateTime CreatedOn);
}
