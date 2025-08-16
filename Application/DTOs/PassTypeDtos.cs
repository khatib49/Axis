namespace Application.DTOs
{
    public record PassTypeDto(Guid Id, string Name, int DurationInMinutes, decimal Price, Guid GameId);
    public record PassTypeCreateDto(string Name, int DurationInMinutes, decimal Price, Guid GameId);
    public record PassTypeUpdateDto(string? Name, int? DurationInMinutes, decimal? Price, Guid? GameId);
}
