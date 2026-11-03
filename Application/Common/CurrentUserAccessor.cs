using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Application.Common
{
    public sealed class CurrentUserAccessor : ICurrentUserAccessor
    {
        public const string BranchHeader = "X-Branch-Id";
        private readonly IHttpContextAccessor _http;
        public CurrentUserAccessor(IHttpContextAccessor http) => _http = http;

        private ClaimsPrincipal? User => _http.HttpContext?.User;

        public int? UserId
        {
            get
            {
                var raw = User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? User?.FindFirstValue("sub");
                return int.TryParse(raw, out var id) ? id : null;
            }
        }

        public string UserName =>
            User?.Identity?.Name
            ?? User?.FindFirstValue(ClaimTypes.Email)
            ?? User?.FindFirstValue("email")
            ?? "system";

        public IReadOnlyList<string> Roles =>
            User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList() ?? new List<string>();

        public int? BranchId
        {
            get
            {
                var h = _http.HttpContext?.Request.Headers[BranchHeader].FirstOrDefault();
                return int.TryParse(h, out var id) ? id : null;
            }
        }

        public bool IsInRole(string role) => Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }
}
