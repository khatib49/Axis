namespace Application.DTOs
{
    public record CategoryDto(Guid Id, string Name);
    public record CategoryCreateDto(string Name);
    public record CategoryUpdateDto(string? Name);

    public record ItemDto(Guid Id, string Name, int Quantity, decimal Price, string Type, Guid CategoryId, Guid? GameId);
    public record ItemCreateDto(string Name, int Quantity, decimal Price, string Type, Guid CategoryId, Guid? GameId);
    public record ItemUpdateDto(string? Name, int? Quantity, decimal? Price, string? Type, Guid? CategoryId, Guid? GameId);
}
