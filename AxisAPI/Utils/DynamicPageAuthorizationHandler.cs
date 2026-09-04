using System.Security.Claims;
using Application.IServices;
using Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace AxisAPI.Utils
{
    /// <summary>
    /// Lets custom roles through [Authorize(Roles = "...")] checks.
    ///
    /// Controllers keep their static role lists for the built-in roles. On
    /// top of that, when the caller holds a role that was granted (under
    /// Admin → Roles &amp; Permissions) a page which uses this controller, the
    /// request is authorized too. The page → controller mapping lives in
    /// <see cref="PageCatalog"/>.
    /// </summary>
    public class DynamicPageAuthorizationHandler : AuthorizationHandler<RolesAuthorizationRequirement>
    {
        private readonly IHttpContextAccessor _http;
        private readonly IRolePageService _pages;
        private readonly ILogger<DynamicPageAuthorizationHandler> _logger;

        public DynamicPageAuthorizationHandler(
            IHttpContextAccessor http, IRolePageService pages, ILogger<DynamicPageAuthorizationHandler> logger)
        {
            _http = http;
            _pages = pages;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context, RolesAuthorizationRequirement requirement)
        {
            if (context.HasSucceeded) return;
            if (context.User?.Identity?.IsAuthenticated != true) return;

            var descriptor = _http.HttpContext?.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (descriptor is null) return;

            var keys = PageCatalog.KeysForController(descriptor.ControllerTypeInfo.Name);
            if (keys.Count == 0) return;

            var roles = context.User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToList();
            if (roles.Count == 0) return;

            try
            {
                var granted = await _pages.GetPagesForRolesAsync(roles, _http.HttpContext!.RequestAborted);
                if (keys.Any(granted.Contains))
                    context.Succeed(requirement);
            }
            catch (Exception ex)
            {
                // Never let a permissions lookup failure turn into a 500 on every request.
                _logger.LogWarning(ex, "Dynamic page permission lookup failed for {Controller}", descriptor.ControllerTypeInfo.Name);
            }
        }
    }
}
