namespace Application.DTOs
{
    public record RoomDto(Guid Id, string Name, Guid? CategoryId, string? CategoryName, int? Sets);
    public record RoomCreateDto(string Name, Guid? CategoryId, int? Sets);
    public record RoomUpdateDto(string? Name, Guid? CategoryId, int? Sets);
}
