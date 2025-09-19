using Application.DTOs;

namespace Application.IServices
{
    public interface IRoomService
    {
        Task<RoomDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<PaginatedResponse<RoomDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<RoomDto> CreateAsync(RoomCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, RoomUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
