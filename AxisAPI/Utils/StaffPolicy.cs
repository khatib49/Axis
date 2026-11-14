using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace AxisAPI.Utils
{
    /// <summary>
    /// "Staff" = signed in with at least one role that is not a customer role.
    ///
    /// Customers can create their own account on the public website (role
    /// "client"), so a plain [Authorize] lets any member of the public in.
    /// Endpoints meant for venue staff use [Authorize(Policy = StaffPolicy.Name)]
    /// instead. Custom roles made under Roles &amp; Permissions count as staff.
    /// </summary>
    public static class StaffPolicy
    {
        public const string Name = "Staff";

        /// <summary>Roles that belong to the public, never to staff.</summary>
        public static readonly HashSet<string> CustomerRoles =
            new(StringComparer.OrdinalIgnoreCase) { "client", "user", "customer" };

        public static bool IsStaff(ClaimsPrincipal user) =>
            user.Identity?.IsAuthenticated == true
            && user.FindAll(ClaimTypes.Role).Any(c => !CustomerRoles.Contains(c.Value));

        public static void Add(AuthorizationOptions options) =>
            options.AddPolicy(Name, p => p.RequireAuthenticatedUser().RequireAssertion(ctx => IsStaff(ctx.User)));
    }
}
