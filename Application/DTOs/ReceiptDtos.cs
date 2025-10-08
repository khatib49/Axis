namespace Application.DTOs
{
    public record ReceiptDto(int Id, int TransactionId, int UserId, int CardId, DateTime GeneratedOn, string Content);
    public record ReceiptCreateDto(int TransactionId, int UserId, int CardId, string Content);
    public record ReceiptUpdateDto(string? Content);
}
