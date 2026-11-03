using Application.DTOs;
using Application.IServices;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.OrderingChannels
{
    /// <summary>Public kiosk endpoints, authenticated by header X-Kiosk-Token. Rate limited per IP.</summary>
    [ApiController]
    [Route("api/kiosk")]
    [AllowAnonymous]
    [AnonymousRateLimit(MaxRequests = 30, WindowSeconds = 60)]
    public class KioskController : ControllerBase
    {
        public const string TokenHeader = "X-Kiosk-Token";
        private readonly IKioskService _svc;
        public KioskController(IKioskService svc) { _svc = svc; }

        private string? Token => Request.Headers[TokenHeader].FirstOrDefault();

        private IActionResult FromResponse<T>(BaseResponse<T> r)
        {
            if (r.Success) return Ok(r);
            return r.Error == "Unauthorized" ? Unauthorized(r) : BadRequest(r);
        }

        [HttpGet("menu")]
        public async Task<IActionResult> Menu(CancellationToken ct) => FromResponse(await _svc.GetMenuAsync(Token, ct));

        /// <summary>Creates a Takeaway delivery order + open invoice; returns the order number.</summary>
        [HttpPost("order")]
        public async Task<IActionResult> Order([FromBody] KioskOrderRequest req, CancellationToken ct)
            => FromResponse(await _svc.PlaceOrderAsync(Token, req, ct));
    }
}
