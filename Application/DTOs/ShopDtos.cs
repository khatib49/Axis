namespace Application.DTOs
{
    // ── Customer auth (website) ──────────────────────────────────────────
    public record CustomerRegisterRequest(string FirstName, string LastName, string Phone, string? Email, string Password);
    /// <summary>Identifier = phone number or email.</summary>
    public record CustomerLoginRequest(string Identifier, string Password);
    public record CustomerDto(int Id, string FirstName, string LastName, string Phone, string? Email, decimal WalletBalance);
    public record CustomerAuthResponse(bool Success, string? Token, CustomerDto? Customer, string? Error);

    // ── Public catalogue (online shop page) ──────────────────────────────
    public record ShopCatalogItemDto(
        int Id, string Name, decimal Price, string? ImagePath, int Quantity, string? Description,
        List<ItemVariantDto> Variants, List<ItemAddOnDto> AddOns);
    public record ShopCatalogCategoryDto(int Id, string Name, string? ItemType, List<ShopCatalogItemDto> Items);
    /// <summary>Everything the shop page needs in one call.</summary>
    public record ShopCatalogDto(
        bool ShopEnabled, string Title, bool DeliveryEnabled, bool CodEnabled, bool OnlinePaymentEnabled,
        List<ShopCatalogCategoryDto> Categories,
        List<ShopZoneDto> Zones);
    public record ShopZoneDto(int Id, string Name, List<string> Cities, decimal Fee, decimal? FreeAbove, string? EstimatedDays);

    // ── Cart / checkout ──────────────────────────────────────────────────
    public record ShopCartAddOn(int AddOnId, int Quantity);
    public record ShopCartLine(int ItemId, int Quantity, int? VariantId = null, List<ShopCartAddOn>? AddOns = null);

    /// <summary>Delivery address typed at checkout (Fulfilment = Delivery).</summary>
    public record ShopAddressDto(
        string Line1, string? Line2, string City, string? Region, string? Notes,
        string? ContactName = null, string? ContactPhone = null);

    public record PlaceOrderRequest(
        List<ShopCartLine> Lines,
        /// PayAtPickup | Online | COD
        string PaymentMode,
        string? Notes = null,
        string? PickupTime = null,
        /// Pickup (default) | Delivery
        string? Fulfilment = null,
        ShopAddressDto? Address = null);

    /// <summary>Ask what delivery would cost before placing the order.</summary>
    /// <summary>PaymentMode "COD" makes a live Aramex quote include the COD surcharge — same as the final order.</summary>
    public record ShopQuoteRequest(List<ShopCartLine> Lines, string City, string? PaymentMode = null);
    public record ShopQuoteDto(
        bool Deliverable, string? Error,
        decimal Subtotal, decimal DeliveryFee, decimal Total, decimal WeightKg,
        /// zone | aramex | free
        string RateSource, string? ZoneName, string? EstimatedDays, int? ZoneId);

    public record OnlineOrderLineDto(
        int Id, int ItemId, string ItemName, decimal UnitPrice, int Quantity,
        int? VariantId, string? VariantName, decimal VariantPriceDelta,
        List<OrderLineAddOnDto> AddOns, decimal LineTotal, string? ImagePath);

    public record OnlineOrderDto(
        int Id, string Code, int UserId, string CustomerName, string CustomerPhone, string? CustomerEmail,
        string Fulfilment, string PaymentMode, string Status,
        decimal Subtotal, decimal Total, string? Notes, string? PickupTime,
        int? TransactionRecordId, int? OnlinePaymentId, string? PayUrl,
        string? HandledBy, string? CancelReason,
        DateTime CreatedOn, DateTime? PaidOn, DateTime? AcceptedOn, DateTime? ReadyOn, DateTime? CompletedOn,
        List<OnlineOrderLineDto> Lines,
        /// Minutes since the order was placed — for the till inbox.
        int AgeMinutes,
        // ── Delivery (null / 0 for pickup) ──
        decimal DeliveryFee = 0m,
        string? RateSource = null,
        ShopAddressDto? Address = null,
        decimal? WeightKg = null,
        DateTime? ShippedOn = null,
        DateTime? DeliveredOn = null,
        ShipmentDto? Shipment = null);

    public record PlaceOrderResult(bool Success, OnlineOrderDto? Order, string? PayUrl, string? Error);

    public record OnlineOrderInboxDto(int NewCount, int AcceptedCount, int ReadyCount, List<OnlineOrderDto> Orders,
        int ToShipCount = 0, int InTransitCount = 0);
}
