namespace Application.DTOs
{
    public record GameDto(Guid Id, string Name, string Type, DateTime CreatedOn, DateTime? ModifiedOn);
    public record GameCreateDto(string Name, string Type);
    public record GameUpdateDto(string? Name, string? Type);
}
