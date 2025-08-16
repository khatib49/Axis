using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/auth")]

    public class AuthController : ControllerBase
    {
        private readonly IAuthService _auth;
        public AuthController(IAuthService auth) => _auth = auth;

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterRequest req, CancellationToken ct)
        {
            var res = await _auth.CreateUserWithRoleAsync(req, ct);
            if (!res.Success) return BadRequest(new { error = res.Error });
            return Ok(new { message = "User created and role assigned." });
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
