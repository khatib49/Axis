namespace Application.DTOs
{
    public record GameDto(Guid Id, string Name, string Type, Guid? StatusId, DateTime CreatedOn, DateTime? ModifiedOn);
    public record GameCreateDto(string Name, string Type, Guid? StatusId);
    public record GameUpdateDto(string? Name, string? Type, Guid? StatusId);
}
