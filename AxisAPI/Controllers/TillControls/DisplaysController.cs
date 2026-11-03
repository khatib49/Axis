using Application.DTOs;
using Application.IServices;
using AxisAPI.Controllers.Payments2;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.TillControls
{
    /// <summary>
    /// Customer-facing displays: admin manages one per register (token in the
    /// screen URL), the till pushes the cart / asks for a Whish QR, and the
    /// screen polls the anonymous view with its token.
    /// </summary>
    [ApiController]
    [Route("api/till/displays")]
    public class DisplaysController : ControllerBase
    {
        private readonly IDisplayStateService _svc;
        public DisplaysController(IDisplayStateService svc) => _svc = svc;

        // ── Admin ─────────────────────────────────────────────────────────
        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create([FromBody] DisplayCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/rotate-token")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Rotate(int id, CancellationToken ct)
        {
            var r = await _svc.RotateTokenAsync(id, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();

        // ── Till ──────────────────────────────────────────────────────────
        /// <summary>Replace what the register's screen shows; totals recomputed in USD + LBP.</summary>
        [HttpPost("{register}/state")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> Push(string register, [FromBody] DisplayPushDto dto, CancellationToken ct)
        {
            var r = await _svc.PushAsync(register, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        /// <summary>Start a Whish request for the register's invoice and show the QR on the screen (source Display).</summary>
        [HttpPost("{register}/request-whish")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> RequestWhish(string register, [FromBody] DisplayRequestWhishDto? dto, CancellationToken ct)
        {
            var (ok, ko) = WhishReturnUrls.Build(Request);
            var r = await _svc.RequestWhishAsync(register, dto ?? new DisplayRequestWhishDto(), ok, ko, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        // ── Screen ────────────────────────────────────────────────────────
        /// <summary>Polled by the display screen every couple of seconds.</summary>
        [HttpGet("view/{token}")]
        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 120, WindowSeconds = 60)]
        public async Task<IActionResult> View(string token, CancellationToken ct)
        {
            var r = await _svc.ViewAsync(token, ct);
            return r.Success ? Ok(r) : Unauthorized(r);
        }
    }
}
