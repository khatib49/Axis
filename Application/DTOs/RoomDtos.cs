namespace Application.DTOs
{
    public record RoomDto(Guid Id, string Name, bool IsAvailable, Guid GameId, Guid? AssignedUserId, DateTime? CurrentSessionStartTime);
    public record RoomCreateDto(string Name, Guid GameId, Guid? AssignedUserId);
    public record RoomUpdateDto(string? Name, bool? IsAvailable, Guid? AssignedUserId);
}
