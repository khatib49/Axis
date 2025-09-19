namespace Application.DTOs
{
    public record TransactionDto(Guid Id, string Reference, string Type, decimal Price, Guid UserId, Guid CardId, Guid StatusId);
    public record TransactionCreateDto(string Reference, string Type, decimal Price, Guid UserId, Guid CardId, Guid? StatusId);
    public record TransactionUpdateDto(string? Reference, string? Type, decimal? Price, Guid? CardId, Guid? StatusId);
}
