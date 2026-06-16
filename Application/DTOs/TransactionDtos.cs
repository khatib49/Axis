namespace Application.DTOs
{
    public record DailySalesDto(
    DateTime Date,       
    decimal ItemsTotal,
    decimal GamesTotal,
    decimal GrandTotal
);
    public record BaseResponse<T>(bool Success, string? Error, string Message, T? Data = default);
    public record PeriodTotalsDto(decimal TotalAmount, int OrdersCount);
    public record TransactionDto(
        int Id,
        int? RoomId,
        string Room,
        int? GameTypeId,
        string GameType,
        int? GameId,
        string Game,
        int? GameSettingId,
        string GameSetting,
        decimal Hours,
        decimal TotalPrice,
        int StatusId,
        DateTime CreatedOn,
        DateTime? ModifiedOn,
        string CreatedBy,
        List<TransactionItemDto> Items,
        int? SetId,
        string Set,
        int? DiscountId,
        int? DiscountPercentage,
        string? DiscountName,      // <-- added
        int numberOfPersons,
        bool IsDayPass,
        string? Comment,
        int? UserId,
        string? UserName,
        // Sales channel the F&B order came through (Toters, etc.).
        // Null = in-house / direct. Trailing nullable defaults keep this
        // field backward-compatible with positional callers.
        int? ChannelId = null,
        string? ChannelName = null,
        // Stock warnings produced by the sale — non-null only when at least
        // one ingredient went negative. The cashier UI shows these as a
        // yellow toast naming the affected ingredients.
        List<StockConsumptionWarningDto>? StockWarnings = null
     );
    public record RemoveItemFromInvoiceDto(int ItemId);
    public record UpdateSetRequest(int? SetId);
    public record CreateCoffeeShopOrderRequest(
    int? UserId,
    List<OrderItemRequest> ItemsRequest,
    int DiscountId,
    bool IsOpenInvoice,
    int? setId,
    string? Comment,
    // Optional sales channel (e.g. Toters). Sent by the cashier UI when the
    // order originated externally. Trailing nullable default keeps existing
    // callers (any older clients still passing positional args) compatible.
    int? ChannelId = null
);

    public record TransactionCreateDto(
        int SetId,
        int RoomId,
    int GameTypeId,
    int GameId,
    int GameSettingId,
    decimal Hours,
    decimal TotalPrice,
    int StatusId,
    int? UserId,
    DateTime CreatedOn,
    string CreatedBy,
    int? DiscountId,
    int numberOfPersons,
        string? Comment
        );

    public record OrderItemRequest(int ItemId, int Quantity);

    // Main-dashboard transactions filter: by created-date range and optional
    // ChannelId. Used by the new "Transactions" card on the home dashboard
    // (with Excel export). Page/PageSize are optional — pass null for an
    // unpaged export-style fetch.
    public record DashboardTransactionsFilterDto(
        DateTime? From,
        DateTime? To,
        int? ChannelId,
        int? Page = 1,
        int? PageSize = 20
    );

    public record DashboardTransactionRowDto(
        int Id,
        DateTime CreatedOn,
        string CreatedBy,
        int StatusId,
        decimal TotalPrice,
        int? ChannelId,
        string? ChannelName,
        string? Comment,
        int ItemsCount
    );
    public record TransactionUpdateDto(
    int? RoomId,    
    int? GameTypeId,
    int? GameId,
    int? GameSettingId,
    int? Hours,
    decimal? TotalPrice,
    int? StatusId,
    int? SetId,
    int? DiscountId
        );

    public record TransactionItemDto(
    int ItemId,
    string ItemName,
    int Quantity,
    decimal Price,
    string Type,
    List<CoffeeShopOrderDto> CoffeeShopOrders // NEW
)   ;
}
