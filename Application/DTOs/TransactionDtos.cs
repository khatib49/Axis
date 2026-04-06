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
        string? UserName
     );
    public record RemoveItemFromInvoiceDto(int ItemId);
    public record UpdateSetRequest(int? SetId);
    public record CreateCoffeeShopOrderRequest(
    int? UserId,
    List<OrderItemRequest> ItemsRequest,
    int DiscountId,
    bool IsOpenInvoice,
    int? setId,
    string? Comment
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
