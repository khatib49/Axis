using System.Text.Json;

namespace Application.DTOs
{
    /// <summary>Supported offline op types. Payload shapes mirror the existing till endpoints.</summary>
    public static class OfflineOpTypes
    {
        /// <summary>Payload = <see cref="CreateCoffeeShopOrderRequest"/> (same JSON as POST api/transactions/CreateCoffeeShopOrder).</summary>
        public const string CreateCoffeeShopOrder = "CreateCoffeeShopOrder";
        /// <summary>Payload = <see cref="AddItemsToOpenInvoicePayload"/>.</summary>
        public const string AddItemsToOpenInvoice = "AddItemsToOpenInvoice";
        /// <summary>Payload = <see cref="CloseOpenInvoicePayload"/>.</summary>
        public const string CloseOpenInvoice = "CloseOpenInvoice";

        public static readonly string[] All = { CreateCoffeeShopOrder, AddItemsToOpenInvoice, CloseOpenInvoice };
    }

    /// <summary>Outcome values of <see cref="OfflineOpResult.Outcome"/>.</summary>
    public static class OfflineOpOutcomes
    {
        public const string Ok = "ok";             // executed now, succeeded
        public const string Replayed = "replayed"; // stored response returned, not re-executed
        public const string Failed = "failed";     // executed (or rejected) and failed; safe to retry
        public const string Conflict = "conflict"; // same opId re-used with a different payload (409)
    }

    /// <summary>
    /// Body of POST api/transactions/AddItemsToOpenInvoice/{invoiceId} plus the
    /// invoice id (route value on the live endpoint). <paramref name="InvoiceOpId"/>
    /// may name the opId of a queued CreateCoffeeShopOrder when the invoice was
    /// itself created offline (client only had a temp id).
    /// </summary>
    public record AddItemsToOpenInvoicePayload(int InvoiceId, List<OrderItemRequest> ItemsRequest, Guid? InvoiceOpId = null);

    /// <summary>Route + query of POST api/transactions/CloseOpenInvoice/{invoiceId}?walletAmount=.</summary>
    public record CloseOpenInvoicePayload(int InvoiceId, decimal WalletAmount = 0, Guid? InvoiceOpId = null);

    public record OfflineOpRequest(Guid OpId, string Type, JsonElement Payload, DateTime? QueuedAt);

    public record OfflineSyncRequest(string ClientId, List<OfflineOpRequest> Ops);

    public record OfflineOpResult(Guid OpId, int Status, string Outcome, JsonElement? Result, string? Error);

    public record OfflineSyncResponse(string ClientId, int BatchId, DateTime ServerTime, List<OfflineOpResult> Results);

    /// <summary>Compact bundle cached by the till for offline use.</summary>
    public record OfflineSnapshotDto(
        string Version,
        DateTime ServerTime,
        IReadOnlyList<CategoryDto> Categories,
        IReadOnlyList<ItemDto> Items,
        IReadOnlyList<DiscountDto> Discounts,
        IReadOnlyList<ChannelDto> Channels);
}
