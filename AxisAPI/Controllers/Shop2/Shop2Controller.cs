using System.Security.Claims;
using Application.DTOs;
using Application.IServices;
using AxisAPI.Controllers.Payments2;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Shop2
{
    /// <summary>
    /// Storefront browsing (anonymous, rate limited) + the customer's
    /// order→invoice bridge for Whish. Catalogue, auth, quote, checkout and
    /// orders stay on the pre-existing api/shop (ShopController).
    /// </summary>
    [ApiController]
    [Route("api/shop2")]
    public class Shop2Controller : ControllerBase
    {
        private readonly IShop2Service _svc;
        public Shop2Controller(IShop2Service svc) { _svc = svc; }

        private int? UserId
        {
            get
            {
                var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
                return int.TryParse(raw, out var id) ? id : null;
            }
        }

        /// <summary>Shop categories with item counts (only categories that currently sell something).</summary>
        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 120, WindowSeconds = 60)]
        [HttpGet("categories")]
        public async Task<IActionResult> Categories(CancellationToken ct) => Ok(await _svc.CategoriesAsync(ct));

        /// <summary>Paged listing. sort = name | priceAsc | priceDesc | newest.</summary>
        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 120, WindowSeconds = 60)]
        [HttpGet("items")]
        public async Task<IActionResult> Items([FromQuery] int? categoryId, [FromQuery] string? search, [FromQuery] int page = 1,
            [FromQuery] int pageSize = 24, [FromQuery] string? sort = null, [FromQuery] bool inStockOnly = false, CancellationToken ct = default)
            => Ok(await _svc.ItemsAsync(new ShopItemListRequest(categoryId, search, page, pageSize, sort, inStockOnly), ct));

        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 120, WindowSeconds = 60)]
        [HttpGet("items/{id:int}")]
        public async Task<IActionResult> Item(int id, CancellationToken ct)
        {
            var i = await _svc.ItemAsync(id, ct);
            return i is null ? NotFound(new { error = "This item is not sold online." }) : Ok(i);
        }

        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 120, WindowSeconds = 60)]
        [HttpGet("featured")]
        public async Task<IActionResult> Featured([FromQuery] int take = 8, CancellationToken ct = default) => Ok(await _svc.FeaturedAsync(take, ct));

        /// <summary>The invoice behind my order — 404 until the shop has created one (never creates it here).</summary>
        [Authorize(Roles = "client")]
        [HttpGet("orders/{code}/invoice")]
        public async Task<IActionResult> OrderInvoice(string code, CancellationToken ct)
        {
            if (UserId is not int uid) return Unauthorized();
            var inv = await _svc.OrderInvoiceAsync(uid, code, ct);
            return inv is null ? NotFound(new { error = "No invoice for this order yet." }) : Ok(inv);
        }

        /// <summary>Pay my accepted (open-invoice) order with Whish; same hosted-checkout flow as self-order.</summary>
        [Authorize(Roles = "client")]
        [HttpPost("orders/{code}/whish")]
        public async Task<IActionResult> StartWhish(string code, [FromBody] ShopWhishStartRequest? body, CancellationToken ct)
        {
            if (UserId is not int uid) return Unauthorized();
            var (ok, ko) = WhishReturnUrls.Build(Request);
            var r = await _svc.StartWhishAsync(uid, code, ok, ko, SafeReturnUrl(body?.ReturnUrl), ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        private static string? SafeReturnUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var u = url.Trim();
            if (u.StartsWith("/") && !u.StartsWith("//")) return u;
            return Uri.TryCreate(u, UriKind.Absolute, out var abs) && (abs.Scheme == "http" || abs.Scheme == "https") ? u : null;
        }
    }
}
