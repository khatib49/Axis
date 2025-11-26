using Application.DTOs;
using Application.DTOs.RequestDto;
using Application.DTOs.ResponseDto;

namespace Application.IServices
{
    public interface IUsersService
    {
        Task<int> CountClientUsersAsync(CancellationToken ct = default);
        Task<UserDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<UserDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<bool> UpdateAsync(int Id, UserUpdateDto request, CancellationToken ct = default);
        Task<ClientUserResponse> CreateClient(ClientUserCreateRequest request, CancellationToken ct = default);
        Task<PaginatedResponse<User2Dto>> GetUsersByRoleIdAsync(
   int roleId,
   BasePaginationRequestDto pagination,
   CancellationToken ct = default);
        Task<List<User2Dto>> SearchByPhoneAsync(string phone, CancellationToken ct = default);
        Task<ClientUserResponse?> UpdateClientAsync(int id, ClientUserUpdateRequest request, CancellationToken ct = default);
    }
}
