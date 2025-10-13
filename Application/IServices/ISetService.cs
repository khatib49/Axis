using Application.DTOs;

namespace Application.IServices
{
    public interface ISetService
    {
        Task<RoomSetDto?> GetAsync(int id, CancellationToken ct = default);

        Task<PaginatedResponse<RoomSetDto>> ListAsync(RoomSetListFilterDto f, CancellationToken ct = default);

        Task<RoomSetDto> CreateAsync(RoomSetCreateDto dto, CancellationToken ct = default);

        Task<bool> UpdateAsync(int id, RoomSetUpdateDto dto, CancellationToken ct = default);

        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
