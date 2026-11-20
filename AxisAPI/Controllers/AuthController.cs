using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/auth")]

    public class AuthController : ControllerBase
    {
        private readonly IAuthService _auth;
        private readonly IPasswordResetService _reset;
        public AuthController(IAuthService auth, IPasswordResetService reset)
        {
            _auth = auth;
            _reset = reset;
        }

        /// <summary>
        /// Emails a reset link if an active account uses this email. Always the
        /// same answer, so it can't be used to find out which emails exist.
        /// </summary>
        [HttpPost("forgot-password")]
        [AllowAnonymous]
        [AxisAPI.Utils.AnonymousRateLimit(MaxRequests = 5, WindowSeconds = 300)]
        public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req, CancellationToken ct)
        {
            await _reset.RequestAsync(req.Email, ct);
            return Ok(new { message = "If an account uses that email, a reset link is on its way." });
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        [AxisAPI.Utils.AnonymousRateLimit(MaxRequests = 10, WindowSeconds = 300)]
        public async Task<IActionResult> ResetPassword(ResetPasswordRequest req, CancellationToken ct)
        {
            var res = await _reset.ResetAsync(req.Email, req.Token, req.NewPassword, ct);
            if (!res.Success) return BadRequest(new { error = res.Error });
            return Ok(new { message = "Password updated. You can sign in now." });
        }



        [Authorize(Roles = "admin")]
        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
        {
            var res = await _auth.CreateUserWithRoleAsync(req, ct);
            if (!res.Success) return BadRequest(new { error = res.Error });
            return Ok(new { message = "User created and role assigned Done." });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct)
        {
            var res = await _auth.LoginAsync(req, ct);
            if (!res.Success) return Unauthorized(new { error = res.Error });
            return Ok(new { token = res.Token });
        }
    }
}
