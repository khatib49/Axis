namespace Application.DTOs
{
    public class SetDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public class RoomSetsAvailabilityDto
    {
        public int RoomId { get; set; }
        public List<SetDto> Available { get; set; } = new();
        public List<SetDto> Unavailable { get; set; } = new();
        public int AvailableCount => Available.Count;
        public int UnavailableCount => Unavailable.Count;
    }
    public record RoomSetDto(int Id, int RoomId, string RoomName, string Name);

    public record RoomSetCreateDto(int RoomId, string Name);

    public record RoomSetUpdateDto(int? RoomId, string? Name);

    public record RoomSetListFilterDto(
        int Page = 1,
        int PageSize = 20,
        int? RoomId = null,
        string? Search = null
    );
}
