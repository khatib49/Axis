using Application.DTOs;

namespace Application.IServices
{
    public interface IAuthService
    {
        Task<BaseResponse> CreateUserWithRoleAsync(RegisterRequest req, CancellationToken ct = default);
        Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct = default);
        /// <summary>Issue a JWT for an already-authenticated user (website customers).</summary>
        Task<string> IssueTokenAsync(Domain.Identity.AppUser user, TimeSpan? lifetime = null);
    }
}
