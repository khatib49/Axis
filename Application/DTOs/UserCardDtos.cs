namespace Application.DTOs
{
    public record UserCardCreateDto(int UserId,int CardId);
    public record UserCardUpdateDto(int UserId,int CardId);
    public record UserCardDto(int Id,int UserId,int CardId);
}
