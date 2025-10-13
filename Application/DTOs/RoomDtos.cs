namespace Application.DTOs
{
    public record RoomDto(int Id, string Name, int? CategoryId, string? CategoryName, int SetCount);
    public record RoomCreateDto(string Name, int? CategoryId, int SetCount);
    public record RoomUpdateDto(string? Name, int? CategoryId, int? SetCount);
}
