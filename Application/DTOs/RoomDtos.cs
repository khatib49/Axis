namespace Application.DTOs
{
    public record RoomDto(int Id, string Name, int? CategoryId, string? CategoryName, int? Sets);
    public record RoomCreateDto(string Name, int? CategoryId, int? Sets);
    public record RoomUpdateDto(string? Name, int? CategoryId, int? Sets);
}
