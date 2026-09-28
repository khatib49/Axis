namespace Application.DTOs
{
    // ── Customer auth (website) ──────────────────────────────────────────
    public record CustomerRegisterRequest(string FirstName, string LastName, string Phone, string? Email, string Password);
    /// <summary>Identifier = phone number or email.</summary>
    public record CustomerLoginRequest(string Identifier, string Password);
    public record CustomerDto(int Id, string FirstName, string LastName, string Phone, string? Email, decimal WalletBalance);
    public record CustomerAuthResponse(bool Success, string? Token, CustomerDto? Customer, string? Error);

    // ── Cart / checkout ──────────────────────────────────────────────────
    public record ShopCartAddOn(int AddOnId, int Quantity);
    public record ShopCartLine(int ItemId, int Quantity, int? VariantId = null, List<ShopCartAddOn>? AddOns = null);
    public record PlaceOrderRequest(List<ShopCartLine> Lines, string PaymentMode, string? Notes = null, string? PickupTime = null);

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
        int AgeMinutes);

    public record PlaceOrderResult(bool Success, OnlineOrderDto? Order, string? PayUrl, string? Error);

    public record OnlineOrderInboxDto(int NewCount, int AcceptedCount, int ReadyCount, List<OnlineOrderDto> Orders);
}
