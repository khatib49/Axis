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
        /// <summary>Payload = <see cref="CreateGameSessionPayload"/> (the query parameters of POST api/transactions/CreateGameSession as a JSON object).</summary>
        public const string CreateGameSession = "CreateGameSession";
        /// <summary>Payload = <see cref="CloseGameSessionPayload"/> (POST api/transactions/sessions/{invoiceId}/close?walletAmount=).</summary>
        public const string CloseGameSession = "CloseGameSession";

        public static readonly string[] All = { CreateCoffeeShopOrder, AddItemsToOpenInvoice, CloseOpenInvoice, CreateGameSession, CloseGameSession };

        /// <summary>Op types that create an invoice; follow-up ops may reference them by opId instead of a server id.</summary>
        public static readonly string[] Creating = { CreateCoffeeShopOrder, CreateGameSession };
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

    /// <summary>
    /// The query parameters of POST api/transactions/CreateGameSession, as a JSON
    /// object. <paramref name="Status"/> is the status id the till sends (1 =
    /// enabled; the service decides the stored status), <paramref name="SetId"/>
    /// 0 for open-set rooms.
    /// </summary>
    public record CreateGameSessionPayload(
        int GameId,
        int GameSettingId,
        int Hours,
        int Status = 1,
        int SetId = 0,
        int DiscountId = 0,
        int? UserId = null,
        int NumberOfPersons = 1,
        bool IsDayPass = false,
        string? Comment = "");

    /// <summary>
    /// Route + query of POST api/transactions/sessions/{invoiceId}/close?walletAmount=.
    /// <paramref name="InvoiceOpId"/> may name the opId of a queued CreateGameSession
    /// when the session was itself started offline (client only had a temp id).
    /// </summary>
    public record CloseGameSessionPayload(int InvoiceId, decimal WalletAmount = 0, Guid? InvoiceOpId = null);

    public record OfflineOpRequest(Guid OpId, string Type, JsonElement Payload, DateTime? QueuedAt);

    public record OfflineSyncRequest(string ClientId, List<OfflineOpRequest> Ops);

    public record OfflineOpResult(Guid OpId, int Status, string Outcome, JsonElement? Result, string? Error);

    public record OfflineSyncResponse(string ClientId, int BatchId, DateTime ServerTime, List<OfflineOpResult> Results);

    /// <summary>A room set with its current status (10 = occupied by an ongoing session, 9 = free after close).</summary>
    public record OfflineRoomSetDto(int Id, string Name, int? StatusId, string? Status);

    /// <summary>A room and its sets, so the till can pick a station offline.</summary>
    public record OfflineRoomDto(int Id, string Name, int? CategoryId, string? CategoryName, bool IsOpenSet, IReadOnlyList<OfflineRoomSetDto> Sets);

    /// <summary>
    /// Compact bundle cached by the till for offline use. The game lists
    /// (games, rooms with sets, active game settings, pass types) are trailing
    /// optional members so earlier callers of the positional constructor keep
    /// compiling; the snapshot service always fills them.
    /// </summary>
    public record OfflineSnapshotDto(
        string Version,
        DateTime ServerTime,
        IReadOnlyList<CategoryDto> Categories,
        IReadOnlyList<ItemDto> Items,
        IReadOnlyList<DiscountDto> Discounts,
        IReadOnlyList<ChannelDto> Channels,
        IReadOnlyList<GameDto>? Games = null,
        IReadOnlyList<OfflineRoomDto>? Rooms = null,
        IReadOnlyList<SettingDto>? GameSettings = null,
        IReadOnlyList<PassTypeDto>? PassTypes = null);
}
