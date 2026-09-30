using System.Security.Claims;
using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    /// <summary>
    /// Website shop: customer accounts + checkout (role "client"), and the
    /// till's online-order inbox (staff roles). One controller so the page
    /// catalog maps it to the "Online Orders" page.
    /// </summary>
    [ApiController]
    [Route("api/shop")]
    public class ShopController : ControllerBase
    {
        private readonly IShopService _shop;
        private readonly IShippingService _shipping;
        private readonly IEventRegistrationService _events;
        public ShopController(IShopService shop, IShippingService shipping, IEventRegistrationService events) { _shop = shop; _shipping = shipping; _events = events; }

        private int? UserId
        {
            get
            {
                var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
                return int.TryParse(raw, out var id) ? id : null;
            }
        }
        private string Actor => User?.Identity?.Name ?? "till";

        // ── Public catalogue (anonymous) ─────────────────────────────────
        /// <summary>Shop page data: categories/items flagged for online sale, switches, delivery zones.</summary>
        [AllowAnonymous]
        [HttpGet("catalog")]
        public async Task<IActionResult> Catalog(CancellationToken ct) => Ok(await _shop.GetCatalogAsync(ct));

        /// <summary>Delivery fee + total for a cart to a city (no order created).</summary>
        [AllowAnonymous]
        [HttpPost("quote")]
        public async Task<IActionResult> Quote([FromBody] ShopQuoteRequest body, CancellationToken ct) => Ok(await _shop.QuoteAsync(body, ct));

        // ── Customer auth (anonymous) ────────────────────────────────────
        [AllowAnonymous]
        [HttpPost("auth/register")]
        public async Task<IActionResult> Register([FromBody] CustomerRegisterRequest body, CancellationToken ct)
        {
            var r = await _shop.RegisterAsync(body, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [AllowAnonymous]
        [HttpPost("auth/login")]
        public async Task<IActionResult> Login([FromBody] CustomerLoginRequest body, CancellationToken ct)
        {
            var r = await _shop.LoginAsync(body, ct);
            return r.Success ? Ok(r) : Unauthorized(r);
        }

        // ── Customer (role client) ───────────────────────────────────────
        [Authorize(Roles = "client")]
        [HttpGet("me")]
        public async Task<IActionResult> Me(CancellationToken ct)
        {
            if (UserId is not int uid) return Unauthorized();
            var me = await _shop.MeAsync(uid, ct);
            return me is null ? Unauthorized() : Ok(me);
        }

        [Authorize(Roles = "client")]
        [HttpPost("orders")]
        public async Task<IActionResult> PlaceOrder([FromBody] PlaceOrderRequest body, CancellationToken ct)
        {
            if (UserId is not int uid) return Unauthorized();
            var r = await _shop.PlaceOrderAsync(uid, body, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [Authorize(Roles = "client")]
        [HttpGet("orders")]
        public async Task<IActionResult> MyOrders(CancellationToken ct)
        {
            if (UserId is not int uid) return Unauthorized();
            return Ok(await _shop.MyOrdersAsync(uid, ct));
        }

        [Authorize(Roles = "client")]
        [HttpGet("orders/{code}")]
        public async Task<IActionResult> MyOrder(string code, CancellationToken ct)
        {
            if (UserId is not int uid) return Unauthorized();
            var o = await _shop.GetMyOrderAsync(uid, code, ct);
            return o is null ? NotFound() : Ok(o);
        }

        /// <summary>Event tickets bought with this account (or with the account's phone number).</summary>
        [Authorize(Roles = "client")]
        [HttpGet("tickets")]
        public async Task<IActionResult> MyTickets(CancellationToken ct)
        {
            if (UserId is not int uid) return Unauthorized();
            var me = await _shop.MeAsync(uid, ct);
            return Ok(await _events.MyTicketsAsync(uid, me?.Phone, ct));
        }

        [Authorize(Roles = "client")]
        [HttpPost("orders/{code}/cancel")]
        public async Task<IActionResult> CancelMine(string code, CancellationToken ct)
        {
            if (UserId is not int uid) return Unauthorized();
            return await _shop.CustomerCancelAsync(uid, code, ct) ? NoContent() : BadRequest(new { error = "This order can no longer be cancelled." });
        }

        // ── Till inbox (staff) ───────────────────────────────────────────
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpGet("inbox")]
        public async Task<IActionResult> Inbox([FromQuery] bool includeDone = false, CancellationToken ct = default)
            => Ok(await _shop.InboxAsync(includeDone, ct));

        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpGet("inbox/{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var o = await _shop.GetAsync(id, ct);
            return o is null ? NotFound() : Ok(o);
        }

        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPost("inbox/{id:int}/accept")]
        public async Task<IActionResult> Accept(int id, CancellationToken ct)
        {
            var (ok, error, order) = await _shop.AcceptAsync(id, Actor, ct);
            return ok ? Ok(order) : BadRequest(new { error });
        }

        public record StatusBody(string Status, string? Reason);

        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPost("inbox/{id:int}/status")]
        public async Task<IActionResult> SetStatus(int id, [FromBody] StatusBody body, CancellationToken ct)
        {
            var (ok, error, order) = await _shop.SetStatusAsync(id, body.Status, Actor, body.Reason, ct);
            return ok ? Ok(order) : BadRequest(new { error });
        }

        // ── Till: delivery actions on an order ───────────────────────────
        /// <summary>Create the Aramex shipment (AWB + label) for an accepted delivery order.</summary>
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPost("inbox/{id:int}/ship")]
        public async Task<IActionResult> Ship(int id, [FromBody] CreateShipmentRequest? body, CancellationToken ct)
        {
            var (ok, error, shipment) = await _shipping.CreateShipmentForOrderAsync(id, body ?? new CreateShipmentRequest(), Actor, ct);
            return ok ? Ok(await _shop.GetAsync(id, ct)) : BadRequest(new { error, shipment });
        }

        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPost("shipments/{shipmentId:int}/refresh")]
        public async Task<IActionResult> RefreshShipment(int shipmentId, CancellationToken ct)
        {
            var (ok, error, shipment) = await _shipping.RefreshTrackingAsync(shipmentId, ct);
            return ok ? Ok(shipment) : BadRequest(new { error, shipment });
        }

        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPost("shipments/{shipmentId:int}/label")]
        public async Task<IActionResult> ShipmentLabel(int shipmentId, CancellationToken ct)
        {
            var (ok, error, shipment) = await _shipping.PrintLabelAsync(shipmentId, ct);
            return ok ? Ok(shipment) : BadRequest(new { error, shipment });
        }

        /// <summary>Manual override when the courier confirms delivery by phone / the poller is late.</summary>
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPost("shipments/{shipmentId:int}/delivered")]
        public async Task<IActionResult> ShipmentDelivered(int shipmentId, CancellationToken ct)
        {
            var (ok, error, shipment) = await _shipping.MarkDeliveredAsync(shipmentId, Actor, ct);
            return ok ? Ok(shipment) : BadRequest(new { error, shipment });
        }

        public record ShipmentStatusBody(string Status, string? Reason);

        /// <summary>Returned / Cancelled / Failed.</summary>
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPost("shipments/{shipmentId:int}/status")]
        public async Task<IActionResult> ShipmentStatus(int shipmentId, [FromBody] ShipmentStatusBody body, CancellationToken ct)
        {
            var (ok, error, shipment) = await _shipping.MarkFailedAsync(shipmentId, body.Status, body.Reason, Actor, ct);
            return ok ? Ok(shipment) : BadRequest(new { error, shipment });
        }

        /// <summary>Book an Aramex courier pickup for every shipment waiting at the shop.</summary>
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPost("shipments/pickup")]
        public async Task<IActionResult> Pickup([FromBody] CreatePickupRequest? body, CancellationToken ct)
        {
            var r = await _shipping.CreatePickupAsync(body ?? new CreatePickupRequest(null, null, null, null, null, null), Actor, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
