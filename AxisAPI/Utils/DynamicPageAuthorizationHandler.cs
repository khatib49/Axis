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

            var controllerName = descriptor.ControllerTypeInfo.Name;

            // New-module controllers live in sub-namespaces (AxisAPI.Controllers.<Module>).
            // For those, an admin-only action may only be unlocked by an admin-grade
            // page; a till page that uses the controller for lookups must not open
            // its admin actions. Original controllers keep their existing behaviour.
            var isNewModule = descriptor.ControllerTypeInfo.Namespace?
                .StartsWith("AxisAPI.Controllers.", StringComparison.Ordinal) == true;
            var adminOnlyAction = isNewModule && IsAdminOnlyAction(descriptor, requirement);

            var keys = adminOnlyAction
                ? PageCatalog.AdminGradeKeysForController(controllerName)
                : PageCatalog.KeysForController(controllerName);
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

        /// <summary>
        /// Class and action [Authorize] attributes combine with AND, so a controller
        /// marked admin-only at class level still demands admin on an action that
        /// only adds [Authorize] or a till role list. The action's own attributes
        /// state its intent: any attribute that admits non-admins makes it a till
        /// action; none, or admin-only ones, make it admin-only.
        /// </summary>
        private static bool IsAdminOnlyAction(ControllerActionDescriptor descriptor, RolesAuthorizationRequirement requirement)
        {
            static bool AdminOnly(string? roles) =>
                !string.IsNullOrWhiteSpace(roles)
                && roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .All(r => string.Equals(r, PageCatalog.AdminRole, StringComparison.OrdinalIgnoreCase));

            var actionAttributes = descriptor.MethodInfo?
                .GetCustomAttributes(inherit: true).OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().ToList()
                ?? new List<Microsoft.AspNetCore.Authorization.IAuthorizeData>();

            if (actionAttributes.Count > 0)
                return actionAttributes.All(a => AdminOnly(a.Roles));

            return requirement.AllowedRoles.Any()
                && requirement.AllowedRoles.All(r => string.Equals(r, PageCatalog.AdminRole, StringComparison.OrdinalIgnoreCase));
        }
    }
}
