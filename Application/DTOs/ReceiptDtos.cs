namespace Application.DTOs
{
    public record ReceiptDto(Guid Id, Guid TransactionId, Guid UserId, Guid CardId, DateTime GeneratedOn, string Content);
    public record ReceiptCreateDto(Guid TransactionId, Guid UserId, Guid CardId, string Content);
    public record ReceiptUpdateDto(string? Content);
}
