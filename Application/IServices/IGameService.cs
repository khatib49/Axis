using Application.DTOs;

namespace Application.IServices
{
    public interface IGameService
    {
        Task<GameDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<List<GameDto>> ListAsync(CancellationToken ct = default);
        Task<GameDto> CreateAsync(GameCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, GameUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
