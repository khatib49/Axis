namespace Application.DTOs
{
    public record RoomDto(int Id, string Name, int? CategoryId, string? CategoryName, int SetCount, bool IsOpenSet);
    public record RoomCreateDto(string Name, int? CategoryId, int SetCount,
        bool IsOpenSet = false);
    public record RoomUpdateDto(string? Name, int? CategoryId, int? SetCount,
        bool? IsOpenSet);
}
