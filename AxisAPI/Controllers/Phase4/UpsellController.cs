using Application.DTOs;
using Application.IServices;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Phase4
{
    /// <summary>
    /// Upsell suggestions. Till roles call suggest / events; guests call the
    /// -public variants (online items only); rules, learning and the report are admin-only.
    /// </summary>
    [ApiController]
    [Route("api/upsell")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class UpsellController : ControllerBase
    {
        private readonly IUpsellService _svc;
        public UpsellController(IUpsellService svc) { _svc = svc; }

        [HttpPost("suggest")]
        public async Task<IActionResult> Suggest([FromBody] UpsellSuggestRequest req, CancellationToken ct)
            => Ok(await _svc.SuggestAsync(req ?? new(new()), false, ct));

        [HttpPost("suggest-public")]
        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 60, WindowSeconds = 60)]
        public async Task<IActionResult> SuggestPublic([FromBody] UpsellSuggestRequest req, CancellationToken ct)
            => Ok(await _svc.SuggestAsync(req ?? new(new()), true, ct));

        [HttpPost("events")]
        public async Task<IActionResult> Event([FromBody] UpsellEventDto dto, CancellationToken ct)
        {
            var r = await _svc.RecordEventAsync(dto, false, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("events-public")]
        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 60, WindowSeconds = 60)]
        public async Task<IActionResult> EventPublic([FromBody] UpsellEventDto dto, CancellationToken ct)
        {
            var r = await _svc.RecordEventAsync(dto, true, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpGet("rules")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Rules(CancellationToken ct) => Ok(await _svc.ListRulesAsync(ct));

        [HttpPost("rules")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateRule([FromBody] UpsellRuleUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateRuleAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("rules/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateRule(int id, [FromBody] UpsellRuleUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateRuleAsync(id, dto, ct);
            return r.Success ? Ok(r) : r.Error == "NotFound" ? NotFound(r) : BadRequest(r);
        }

        [HttpDelete("rules/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteRule(int id, CancellationToken ct)
            => await _svc.DeleteRuleAsync(id, ct) ? NoContent() : NotFound();

        [HttpGet("pairs")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Pairs([FromQuery] int? itemId, [FromQuery] int take = 100, CancellationToken ct = default)
            => Ok(await _svc.ListPairsAsync(itemId, take, ct));

        [HttpPost("rebuild")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Rebuild([FromQuery] int days = 90, [FromQuery] bool pitches = true, CancellationToken ct = default)
            => Ok(await _svc.RebuildAsync(days, pitches, ct));

        [HttpGet("stats")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Stats([FromQuery] int days = 30, CancellationToken ct = default)
            => Ok(await _svc.StatsAsync(days, ct));
    }
}
