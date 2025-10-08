using Application.DTOs;

namespace Application.IServices
{
    public interface IGameService
    {
        Task<GameDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<GameDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<GameDto> CreateAsync(GameCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, GameUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
