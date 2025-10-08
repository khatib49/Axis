using Application.DTOs;

namespace Application.IServices
{
    public interface IUserCardService
    {
        Task<UserCardDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<UserCardDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<UserCardDto> CreateAsync(UserCardCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, UserCardUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
