namespace Application.DTOs
{
    public record StatusDto(Guid Id, string Name);
    public record StatusCreateDto(string Name);
    public record StatusUpdateDto(string? Name);
}
