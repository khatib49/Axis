using System.Reflection;
using System.Security.Claims;
using Application.IServices;
using AxisAPI.Utils;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Tests.Security
{
    /// <summary>
    /// A page grant unlocks a controller for a role. On new-module controllers an
    /// admin-only action must only be unlocked by an admin-grade page, never by a
    /// till page that uses the same controller for its lookups.
    /// </summary>
    public class DynamicPagePermissionTests
    {
        private static readonly string[] TillRoles = { "admin", "cashier", "gamecashier", "admin_fnb" };

        private static async Task<bool> Authorize(Type controller, string role, string[] pagesHeld, string[] allowedRoles, MethodInfo? action = null)
        {
            var http = new DefaultHttpContext();
            var descriptor = new ControllerActionDescriptor { ControllerTypeInfo = controller.GetTypeInfo(), ControllerName = controller.Name, MethodInfo = action! };
            http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(descriptor), "test"));
            var accessor = new Mock<IHttpContextAccessor>();
            accessor.Setup(a => a.HttpContext).Returns(http);

            var pages = new Mock<IRolePageService>();
            pages.Setup(p => p.GetPagesForRolesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(pagesHeld.ToHashSet(StringComparer.OrdinalIgnoreCase));

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, "test"));
            var requirement = new RolesAuthorizationRequirement(allowedRoles);
            var context = new AuthorizationHandlerContext(new[] { requirement }, user, null);

            var handler = new DynamicPageAuthorizationHandler(accessor.Object, pages.Object, NullLogger<DynamicPageAuthorizationHandler>.Instance);
            await handler.HandleAsync(context);
            return context.HasSucceeded;
        }

        [Fact]
        public async Task Cashier_till_page_does_not_unlock_admin_only_actions_on_new_module_controllers()
        {
            (await Authorize(typeof(AxisAPI.Controllers.Retail.BarcodesController), "cashier", new[] { "cashier-items" }, new[] { "admin" }))
                .Should().BeFalse("creating barcodes is admin-only");
        }

        [Fact]
        public async Task Cashier_till_page_still_unlocks_till_actions_on_new_module_controllers()
        {
            // The till action itself already lists cashier, so the static check passes;
            // the handler must not get in the way for a custom till role either.
            (await Authorize(typeof(AxisAPI.Controllers.Retail.BarcodesController), "night_cashier", new[] { "cashier-items" }, TillRoles))
                .Should().BeTrue("barcode lookup is a till action and the role holds the till page");
        }

        [Fact]
        public async Task Custom_role_with_the_admin_page_unlocks_admin_only_actions()
        {
            (await Authorize(typeof(AxisAPI.Controllers.Retail.BarcodesController), "stock_manager", new[] { "retail-barcodes" }, new[] { "admin" }))
                .Should().BeTrue("the role was granted the admin barcode page");
        }

        [Fact]
        public async Task Original_controllers_keep_their_existing_page_grant_behaviour()
        {
            (await Authorize(typeof(AxisAPI.Controllers.TransactionsController), "cashier", new[] { "cashier-items" }, new[] { "admin" }))
                .Should().BeTrue("pre-existing controllers are deliberately left unchanged");
        }

        private static MethodInfo Action(Type controller, Func<MethodInfo, bool> pick) =>
            controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).First(pick);

        private static bool HasAttr<T>(MethodInfo m) where T : Attribute => m.GetCustomAttribute<T>() != null;

        [Fact]
        public async Task Till_action_on_an_admin_class_controller_stays_reachable_through_the_till_page()
        {
            // DeliveryZonesController is admin at class level; its list action adds [Authorize]
            // for the New Delivery Order page. The class admin requirement must still be satisfiable.
            var list = Action(typeof(AxisAPI.Controllers.OrderingChannels.DeliveryZonesController),
                m => HasAttr<Microsoft.AspNetCore.Mvc.HttpGetAttribute>(m) && HasAttr<AuthorizeAttribute>(m));
            (await Authorize(typeof(AxisAPI.Controllers.OrderingChannels.DeliveryZonesController), "cashier", new[] { "delivery-new" }, new[] { "admin" }, list))
                .Should().BeTrue("listing zones is a till action");
        }

        [Fact]
        public async Task Admin_action_on_an_admin_class_controller_is_refused_to_the_till_page()
        {
            var create = Action(typeof(AxisAPI.Controllers.OrderingChannels.DeliveryZonesController),
                m => HasAttr<Microsoft.AspNetCore.Mvc.HttpPostAttribute>(m));
            (await Authorize(typeof(AxisAPI.Controllers.OrderingChannels.DeliveryZonesController), "cashier", new[] { "delivery-new" }, new[] { "admin" }, create))
                .Should().BeFalse("creating zones is admin-only");
            (await Authorize(typeof(AxisAPI.Controllers.OrderingChannels.DeliveryZonesController), "ops_manager", new[] { "delivery-zones" }, new[] { "admin" }, create))
                .Should().BeTrue("the admin zones page unlocks it for a custom role");
        }

        [Fact]
        public async Task Role_without_any_matching_page_is_never_unlocked()
        {
            (await Authorize(typeof(AxisAPI.Controllers.Retail.BarcodesController), "chef", new[] { "kitchen-display" }, TillRoles))
                .Should().BeFalse();
        }
    }
}
