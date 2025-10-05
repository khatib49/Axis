namespace Application.DTOs
{
    public record TransactionDto(
        Guid Id,
        Guid? RoomId,
        string Room,
        Guid? GameTypeId,
        string GameType,
        Guid? GameId,
        string Game,
        Guid? GameSettingId,
        string GameSetting,
        int Hours,
        decimal TotalPrice,
        Guid StatusId,
        DateTime CreatedOn,
        DateTime? ModifiedOn,
        string CreatedBy,
        List<TransactionItemDto> Items
    );


    public record TransactionCreateDto(
        Guid RoomId,
    Guid GameTypeId,
    Guid GameId,
    Guid GameSettingId,
    int Hours,
    decimal TotalPrice,
    Guid StatusId,
    DateTime CreatedOn,
    string CreatedBy
        );

    public record OrderItemRequest(Guid ItemId, int Quantity);
    public record TransactionUpdateDto(Guid RoomId,
    Guid GameTypeId,
    Guid GameId,
    Guid GameSettingId,
    int Hours,
    decimal TotalPrice,
    Guid StatusId);

    public record TransactionItemDto(
    Guid ItemId,
    string ItemName,
    int Quantity,
    decimal Price,
    string Type,
    List<CoffeeShopOrderDto> CoffeeShopOrders // NEW
)   ;
}
