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
}
