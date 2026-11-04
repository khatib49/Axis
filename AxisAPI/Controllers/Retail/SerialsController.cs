using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Retail
{
    /// <summary>
    /// Serial numbers. The till lists in-stock units and records the sale;
    /// receiving, returns, faulty and settings are admin-only.
    /// </summary>
    [ApiController]
    [Route("api/retail/serials")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class SerialsController : ControllerBase
    {
        private readonly ISerialNumberService _svc;
        public SerialsController(ISerialNumberService svc) { _svc = svc; }

        // ── Settings ───────────────────────────────────────────────────
        [HttpGet("settings")]
        public async Task<IActionResult> ListSettings(CancellationToken ct) => Ok(await _svc.ListSettingsAsync(ct));

        [HttpGet("settings/{itemId:int}")]
        public async Task<IActionResult> GetSetting(int itemId, CancellationToken ct)
        {
            var s = await _svc.GetSettingAsync(itemId, ct);
            return s == null ? NotFound() : Ok(s);
        }

        [HttpPut("settings/{itemId:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> SetSetting(int itemId, [FromBody] SerialSettingUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.SetSettingAsync(itemId, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        // ── Units ──────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int? itemId, [FromQuery] string? status, [FromQuery] string? search,
            [FromQuery] int? transactionId, CancellationToken ct)
            => Ok(await _svc.ListAsync(itemId, status, search, transactionId, ct));

        /// <summary>Per-item counts by status next to the item's stock quantity (stock-count helper).</summary>
        [HttpGet("summary")]
        public async Task<IActionResult> Summary([FromQuery] int? itemId, CancellationToken ct) => Ok(await _svc.SummaryAsync(itemId, ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var s = await _svc.GetAsync(id, ct);
            return s == null ? NotFound() : Ok(s);
        }

        [HttpPost("receive")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Receive([FromBody] SerialReceiveDto dto, CancellationToken ct)
        {
            var r = await _svc.ReceiveAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        /// <summary>Till: record which unit left on an invoice. Idempotent for the same invoice.</summary>
        [HttpPost("sell")]
        public async Task<IActionResult> Sell([FromBody] SerialSellDto dto, CancellationToken ct)
        {
            var r = await _svc.SellAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/return")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Return(int id, [FromBody] SerialStatusChangeDto? dto, CancellationToken ct)
        {
            var r = await _svc.ReturnAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/faulty")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Faulty(int id, [FromBody] SerialStatusChangeDto? dto, CancellationToken ct)
        {
            var r = await _svc.MarkFaultyAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/restock")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Restock(int id, [FromBody] SerialStatusChangeDto? dto, CancellationToken ct)
        {
            var r = await _svc.RestockAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
