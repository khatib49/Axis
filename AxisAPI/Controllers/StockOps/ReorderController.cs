using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.StockOps
{
    [ApiController]
    [Route("api/stockops/reorder")]
    [Authorize(Roles = "admin,stock,chef,admin_fnb")]
    public class ReorderController : ControllerBase
    {
        private readonly IReorderService _svc;
        public ReorderController(IReorderService svc) { _svc = svc; }

        [HttpGet("suggestions")]
        public async Task<IActionResult> Suggestions(CancellationToken ct) => Ok(await _svc.GetSuggestionsAsync(ct));

        /// <summary>
        /// Returns a PurchaseCreateDto-shaped payload. Nothing is saved; the
        /// UI stores it and hands it to the existing purchases page.
        /// </summary>
        [HttpPost("create-purchase-draft")]
        public async Task<IActionResult> CreatePurchaseDraft([FromBody] ReorderPurchaseDraftRequestDto? request, CancellationToken ct)
        {
            var r = await _svc.BuildPurchaseDraftAsync(request ?? new ReorderPurchaseDraftRequestDto(null, null), ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpGet("rules")]
        public async Task<IActionResult> Rules(CancellationToken ct) => Ok(await _svc.ListRulesAsync(ct));

        [HttpGet("rules/{id:int}")]
        public async Task<IActionResult> GetRule(int id, CancellationToken ct)
        {
            var r = await _svc.GetRuleAsync(id, ct);
            return r == null ? NotFound() : Ok(r);
        }

        [HttpPost("rules")]
        public async Task<IActionResult> CreateRule([FromBody] ReorderRuleUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateRuleAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("rules/{id:int}")]
        public async Task<IActionResult> UpdateRule(int id, [FromBody] ReorderRuleUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateRuleAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("rules/{id:int}")]
        public async Task<IActionResult> DeleteRule(int id, CancellationToken ct)
            => await _svc.DeleteRuleAsync(id, ct) ? NoContent() : NotFound();
    }
}
