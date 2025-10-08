namespace Application.DTOs
{
    public record GameSessionDto(
    int Id, int UserId, int CardId, int GameId, int RoomId, int PassTypeId,
    DateTime StartTime, DateTime? EndTime, bool IsOpenTime, string Status);

    public record GameSessionCreateDto(
        int UserId, int CardId, int GameId, int RoomId, int PassTypeId,
        DateTime? StartTime, bool IsOpenTime);

    public record GameSessionUpdateDto(
        DateTime? EndTime, bool? IsOpenTime, string? Status, int? RoomId, int? PassTypeId);
}
