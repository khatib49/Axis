namespace Application.DTOs
{
    public record CategoryDto(Guid Id, string Name , string Type);
    public record CategoryCreateDto(string Name , string Type);
    public record CategoryUpdateDto(string? Name , string Type);

    public record ItemDto(Guid Id, string Name, int Quantity, decimal Price, string Type, Guid CategoryId, Guid? StatusId);
    public record ItemCreateDto(string Name, int Quantity, decimal Price, string Type, Guid CategoryId, Guid? StatusId);
    public record ItemUpdateDto(string? Name, int? Quantity, decimal? Price, string? Type, Guid? CategoryId, Guid? StatusId);
}
