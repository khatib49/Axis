namespace Application.DTOs
{
    public record StatusDto(int Id, string Name);
    public record StatusCreateDto(string Name);
    public record StatusUpdateDto(string? Name);
}
