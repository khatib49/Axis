namespace Application.DTOs
{
    public record GameDto(int Id, string Name, int CategoryId, string? CategoryName, int? StatusId, string Status, DateTime? CreatedOn, DateTime? ModifiedOn);
    public record GameCreateDto(string Name, int CategoryId, int? StatusId);
    public record GameUpdateDto(string? Name, int? CategoryId, int? StatusId);
}
