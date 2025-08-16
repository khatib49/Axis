namespace Application.DTOs
{
    public record NotificationDto(Guid Id, Guid UserId, string Title, string Body, string Type, DateTime CreatedOn, bool IsRead);
    public record NotificationCreateDto(Guid UserId, string Title, string Body, string Type);
    public record NotificationUpdateDto(string? Title, string? Body, string? Type, bool? IsRead);
}
