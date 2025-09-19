using Application.DTOs;

namespace Application.IServices
{
    public interface IUsersService
    {
        Task<UserDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<PaginatedResponse<UserDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid Id, UserUpdateDto request, CancellationToken ct = default);

    }
}
