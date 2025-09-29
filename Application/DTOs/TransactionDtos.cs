namespace Application.DTOs
{
    public record TransactionDto(
    Guid Id,
    Guid RoomId,
    string Room,
    Guid GameTypeId,
    string GameType,
    Guid GameId,
    string Game,
    Guid GameSettingId,
    string GameSetting,
    int Hours,
    decimal TotalPrice,
    Guid StatusId,
    DateTime CreatedOn,
    DateTime? ModifiedOn,
    string CreatedBy
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


    public record TransactionUpdateDto(Guid RoomId,
    Guid GameTypeId,
    Guid GameId,
    Guid GameSettingId,
    int Hours,
    decimal TotalPrice,
    Guid StatusId);
}
