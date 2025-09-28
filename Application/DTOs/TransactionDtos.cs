namespace Application.DTOs
{
    public record TransactionDto(
    Guid Id,
    Guid RoomId,
    Guid GameTypeId,
    Guid GameId,
    Guid GameSettingId,
    int Hours,
    decimal TotalPrice,
    Guid StatusId,
    DateTime CreatedOn,
    DateTime? ModifiedOn,
    string CreatedBy
);


    public record TransactionCreateDto(string Reference, string Type, decimal Price, Guid UserId, Guid CardId, Guid? StatusId);
    public record TransactionUpdateDto(string? Reference, string? Type, decimal? Price, Guid? CardId, Guid? StatusId);
}
