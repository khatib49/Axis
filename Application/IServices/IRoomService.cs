using Application.DTOs;

namespace Application.IServices
{
    public interface IRoomService
    {
        Task<RoomDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<RoomDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<RoomDto> CreateAsync(RoomCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, RoomUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
