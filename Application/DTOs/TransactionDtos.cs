namespace Application.DTOs
{
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
        int Hours,
        decimal TotalPrice,
        int StatusId,
        DateTime CreatedOn,
        DateTime? ModifiedOn,
        string CreatedBy,
        List<TransactionItemDto> Items,
        int? SetId,
        string Set
    );


    public record TransactionCreateDto(
        int SetId,
        int RoomId,
    int GameTypeId,
    int GameId,
    int GameSettingId,
    int Hours,
    decimal TotalPrice,
    int StatusId,
    DateTime CreatedOn,
    string CreatedBy
        );

    public record OrderItemRequest(int ItemId, int Quantity);
    public record TransactionUpdateDto(int RoomId,
    int GameTypeId,
    int GameId,
    int GameSettingId,
    int Hours,
    decimal TotalPrice,
    int StatusId,
        int SetId
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
