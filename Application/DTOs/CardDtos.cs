namespace Application.DTOs
{
    public record CardDto(Guid Id, string CardName, string CardType, bool IsActive, DateTime CreatedOn, DateTime? ModifiedOn);
    public record CardCreateDto(string CardName, string CardType);
    public record CardUpdateDto(string? CardName, string? CardType, bool? IsActive);
}
