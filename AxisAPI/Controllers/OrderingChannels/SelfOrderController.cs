using Application.DTOs;
using Application.IServices;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.OrderingChannels
{
    /// <summary>Public QR self-ordering endpoints (guest phone). Rate limited per IP.</summary>
    [ApiController]
    [Route("api/selforder")]
    [AllowAnonymous]
    [AnonymousRateLimit(MaxRequests = 30, WindowSeconds = 60)]
    public class SelfOrderController : ControllerBase
    {
        private readonly ISelfOrderService _svc;
        public SelfOrderController(ISelfOrderService svc) { _svc = svc; }

        private IActionResult FromResponse<T>(BaseResponse<T> r)
        {
            if (r.Success) return Ok(r);
            return r.Error == "NotFound" ? NotFound(r) : BadRequest(r);
        }

        /// <summary>Menu for an active table (categories + items).</summary>
        [HttpGet("menu/{tableCode}")]
        public async Task<IActionResult> Menu(string tableCode, CancellationToken ct)
            => FromResponse(await _svc.GetMenuAsync(tableCode, ct));

        /// <summary>Starts a guest session; returns the token the phone keeps.</summary>
        [HttpPost("session/{tableCode}")]
        public async Task<IActionResult> Start(string tableCode, CancellationToken ct)
            => FromResponse(await _svc.StartSessionAsync(tableCode, ct));

        /// <summary>Sends items: first round opens the invoice, later rounds append to it.</summary>
        [HttpPost("session/{token:guid}/submit")]
        public async Task<IActionResult> Submit(Guid token, [FromBody] SelfOrderSubmitDto dto, CancellationToken ct)
            => FromResponse(await _svc.SubmitAsync(token, dto, ct));

        /// <summary>Session status: items, total, paid flag.</summary>
        [HttpGet("session/{token:guid}")]
        public async Task<IActionResult> Status(Guid token, CancellationToken ct)
            => FromResponse(await _svc.GetStatusAsync(token, ct));
    }
}
