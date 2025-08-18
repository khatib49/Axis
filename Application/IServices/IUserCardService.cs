using Application.DTOs;

namespace Application.IServices
{
    public interface IUserCardService
    {
        Task<UserCardDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<List<UserCardDto>> ListAsync(CancellationToken ct = default);
        Task<UserCardDto> CreateAsync(UserCardCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, UserCardUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
