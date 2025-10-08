using Application.DTOs;

namespace Application.IServices
{
    public interface IUsersService
    {
        Task<UserDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<UserDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<bool> UpdateAsync(int Id, UserUpdateDto request, CancellationToken ct = default);

    }
}
