namespace Application.DTOs
{
    public record ExpenseDto(int Id, string Category, decimal Amount, string? Description, DateTime CreatedOn);
    public record ExpenseCreateDto(string Category, decimal Amount, string? Description);
    public record ExpenseUpdateDto(string? Category, decimal? Amount, string? Description);
}
