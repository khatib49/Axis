using Application.DTOs;

namespace Application.IServices
{
    public interface IGameSessionService
    {
        Task<GameSessionDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<GameSessionDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<GameSessionDto> CreateAsync(GameSessionCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, GameSessionUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
