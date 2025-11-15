namespace Application.DTOs
{
    public record DiscountDto(int Id, string Name, string Type, string? Description, decimal Amount, bool IsActive, DateTime CreatedOn, DateTime UpdatedOn);
    public record DiscountCreateDto(string Name, string Type, string? Description, decimal Amount, bool? IsActive);
    public record DiscountUpdateDto(string? Name, string? Type, string? Description, decimal? Amount, bool? IsActive);
}
