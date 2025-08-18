namespace Application.DTOs
{
    public record UserCardCreateDto(Guid UserId,Guid CardId);
    public record UserCardUpdateDto(Guid UserId,Guid CardId);
    public record UserCardDto(Guid Id,Guid UserId,Guid CardId);
}
