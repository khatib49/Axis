namespace Application.DTOs
{
    public record CategoryDto(int Id, string Name , string Type);
    public record CategoryCreateDto(string Name , string Type);
    public record CategoryUpdateDto(string? Name , string Type);

    public record ItemDto(int Id, string Name, int Quantity, decimal Price, string Type, int CategoryId, int? StatusId, string? ImagePath);
    //public record ItemCreateDto(string Name, int Quantity, decimal Price, string Type, int CategoryId, int? StatusId);
    //public record ItemUpdateDto(string? Name, int? Quantity, decimal? Price, string? Type, int? CategoryId, int? StatusId);
}
