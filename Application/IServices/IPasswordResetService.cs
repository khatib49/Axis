using Application.DTOs;

namespace Application.IServices
{
    /// <summary>"Forgot password" by email: send a one-time link, then set the new password.</summary>
    public interface IPasswordResetService
    {
        /// <summary>
        /// Emails a reset link when an active account has this email. Says
        /// nothing about whether it exists, so callers always answer the same.
        /// </summary>
        Task RequestAsync(string email, CancellationToken ct = default);

        /// <summary>Sets a new password from the emailed link's token.</summary>
        Task<BaseResponse> ResetAsync(string email, string token, string newPassword, CancellationToken ct = default);
    }
}
