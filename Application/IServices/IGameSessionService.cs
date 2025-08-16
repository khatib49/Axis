using Application.DTOs;

namespace Application.IServices
{
    public interface IGameSessionService
    {
        Task<GameSessionDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<List<GameSessionDto>> ListAsync(CancellationToken ct = default);
        Task<GameSessionDto> CreateAsync(GameSessionCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, GameSessionUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
