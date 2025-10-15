namespace Application.DTOs
{
    public record CategoryMenuDto(int Id, string Name, string Type);
    public record ItemMenuDto(int Id, string Name, decimal Price, bool Available, string Type, string? ImagePath);

}
