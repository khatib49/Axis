using Application.DTOs;

namespace Application.IServices
{
    public interface INotificationService
    {
        Task<NotificationDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<List<NotificationDto>> ListAsync(CancellationToken ct = default);
        Task<NotificationDto> CreateAsync(NotificationCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, NotificationUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
