namespace Application.DTOs
{
    public record NotificationDto(int Id, int UserId, string Title, string Body, string Type, DateTime CreatedOn, bool IsRead);
    public record NotificationCreateDto(int UserId, string Title, string Body, string Type);
    public record NotificationUpdateDto(string? Title, string? Body, string? Type, bool? IsRead);
}
