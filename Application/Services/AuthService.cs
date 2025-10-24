using Application.DTOs;
using Application.IServices;
using Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Application.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<AppUser> _userMgr;
        private readonly RoleManager<AppRole> _roleMgr;
        private readonly IConfiguration _cfg;

        public AuthService(UserManager<AppUser> userMgr, RoleManager<AppRole> roleMgr, IConfiguration cfg)
        {
            _userMgr = userMgr;
            _roleMgr = roleMgr;
            _cfg = cfg;
        }

        public async Task<BaseResponse> CreateUserWithRoleAsync(RegisterRequest req, CancellationToken ct = default)
        {
            if (!await _roleMgr.RoleExistsAsync(req.RoleName))
            {
                var createRole = await _roleMgr.CreateAsync(new AppRole { Name = req.RoleName });
                if (!createRole.Succeeded)
                    return new(false, string.Join("; ", createRole.Errors.Select(e => e.Description)));
            }

            var existing = await _userMgr.FindByEmailAsync(req.Email);
            if (existing is not null) return new(false, "Email already in use.");

            var user = new AppUser
            {
                Email = req.Email,
                UserName = req.Email,
                DisplayName = req.DisplayName,
                StatusId = req.StatusId
            };

            var created = await _userMgr.CreateAsync(user, req.Password);
            if (!created.Succeeded)
                return new(false, string.Join("; ", created.Errors.Select(e => e.Description)));

            var added = await _userMgr.AddToRoleAsync(user, req.RoleName);
            if (!added.Succeeded)
                return new(false, string.Join("; ", added.Errors.Select(e => e.Description)));

            return new(true, null);
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest req, CancellationToken ct = default)
        {
            var user = await _userMgr.FindByEmailAsync(req.Email);
            if (user is null) return new(false, null, "Invalid credentials.");

            // ❗ Block if not Active
            if (user.StatusId != 1)
                return new(false, null, "Account is not active.");

            // Also honor lockouts
            if (await _userMgr.IsLockedOutAsync(user))
                return new(false, null, "Account is locked.");

            var ok = await _userMgr.CheckPasswordAsync(user, req.Password);
            if (!ok) return new(false, null, "Invalid credentials.");

            var token = await CreateJwtAsync(user);
            return new(true, token, null);
        }

        private async Task<string> CreateJwtAsync(AppUser user)
        {
            var roles = await _userMgr.GetRolesAsync(user);

            var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.UserName ?? string.Empty),
            new Claim("status", user.StatusId.ToString())
        };

            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

            var issuer = _cfg["Jwt:Issuer"];
            var audience = _cfg["Jwt:Audience"];
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(8),
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
