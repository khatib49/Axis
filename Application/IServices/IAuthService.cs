using Application.DTOs;

namespace Application.IServices
{
    public interface IAuthService
    {
        Task<BaseResponse> CreateUserWithRoleAsync(RegisterRequest req, CancellationToken ct = default);
        Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct = default);
    }
}
