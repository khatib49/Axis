using Application.DTOs;

namespace Application.IServices
{
    public interface INotificationService
    {
        Task<NotificationDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<NotificationDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<NotificationDto> CreateAsync(NotificationCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, NotificationUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
