namespace Application.DTOs
{
    public record GameSessionDto(
    Guid Id, Guid UserId, Guid CardId, Guid GameId, Guid RoomId, Guid PassTypeId,
    DateTime StartTime, DateTime? EndTime, bool IsOpenTime, string Status);

    public record GameSessionCreateDto(
        Guid UserId, Guid CardId, Guid GameId, Guid RoomId, Guid PassTypeId,
        DateTime? StartTime, bool IsOpenTime);

    public record GameSessionUpdateDto(
        DateTime? EndTime, bool? IsOpenTime, string? Status, Guid? RoomId, Guid? PassTypeId);
}
