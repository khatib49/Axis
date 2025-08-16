using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Api.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<AppUser> _userMgr;
        private readonly SignInManager<AppUser> _signInMgr;
        private readonly IConfiguration _cfg;

        public AuthController(UserManager<AppUser> userMgr, SignInManager<AppUser> signInMgr, IConfiguration cfg)
        {
            _userMgr = userMgr; _signInMgr = signInMgr; _cfg = cfg;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            var user = new AppUser { Id = Guid.NewGuid(), UserName = dto.Email, Email = dto.Email, DisplayName = dto.DisplayName };
            var res = await _userMgr.CreateAsync(user, dto.Password);
            if (!res.Succeeded) return BadRequest(res.Errors);
            return Ok();
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login(LoginDto dto)
        {
            var user = await _userMgr.FindByEmailAsync(dto.Email);
            if (user is null) return Unauthorized();
            var chk = await _signInMgr.CheckPasswordSignInAsync(user, dto.Password, false);
            if (!chk.Succeeded) return Unauthorized();

            return Ok(new { token = await CreateJwt(user) });
        }

        private async Task<string> CreateJwt(AppUser user)
        {
            var jwt = _cfg.GetSection("Jwt");
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var roles = await _userMgr.GetRolesAsync(user);
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
                new(ClaimTypes.Name, user.UserName ?? "")
            };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"], audience: jwt["Audience"],
                claims: claims, expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

    public record RegisterDto(string Email, string Password, string? DisplayName);
    public record LoginDto(string Email, string Password);
}
