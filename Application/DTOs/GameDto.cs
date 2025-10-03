namespace Application.DTOs
{
    public record GameDto(Guid Id, string Name, Guid CategoryId, string? CategoryName, Guid? StatusId, string Status, DateTime? CreatedOn, DateTime? ModifiedOn);
    public record GameCreateDto(string Name, Guid CategoryId, Guid? StatusId);
    public record GameUpdateDto(string? Name, Guid? CategoryId, Guid? StatusId);
}
