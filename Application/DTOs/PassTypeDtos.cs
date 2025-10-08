namespace Application.DTOs
{
    public record PassTypeDto(int Id, string Name, int DurationInMinutes, decimal Price, int GameId);
    public record PassTypeCreateDto(string Name, int DurationInMinutes, decimal Price, int GameId);
    public record PassTypeUpdateDto(string? Name, int? DurationInMinutes, decimal? Price, int? GameId);
}
