using System.Text;
using Application.IServices;
using Application.Services.MenuEngineering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.MenuEngineering
{
    /// <summary>Recipe costing and the Star / Plowhorse / Puzzle / Dog matrix.</summary>
    [ApiController]
    [Route("api/menu-engineering")]
    [Authorize(Roles = "admin,admin_fnb")]
    public class MenuEngineeringController : ControllerBase
    {
        private readonly IMenuEngineeringService _svc;
        public MenuEngineeringController(IMenuEngineeringService svc) => _svc = svc;

        /// <summary>
        /// Menu matrix for [from, to). ?categoryIds=1&amp;categoryIds=2 scopes the menu,
        /// ?marginThresholdPct=60 drives the low-margin flag.
        /// </summary>
        [HttpGet("report")]
        public async Task<IActionResult> Report(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] List<int>? categoryIds, [FromQuery] int? branchId,
            [FromQuery] decimal marginThresholdPct = MenuEngineeringService.DefaultMarginThresholdPct,
            CancellationToken ct = default)
        {
            if (from.HasValue && to.HasValue && to < from) return BadRequest(new { error = "'to' must be after 'from'" });
            return Ok(await _svc.GetReportAsync(from, to, categoryIds is { Count: > 0 } ? categoryIds : null, branchId, marginThresholdPct, ct));
        }

        /// <summary>Same report as text/csv (download).</summary>
        [HttpGet("report/export.csv")]
        public async Task<IActionResult> ExportCsv(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] List<int>? categoryIds, [FromQuery] int? branchId,
            [FromQuery] decimal marginThresholdPct = MenuEngineeringService.DefaultMarginThresholdPct,
            CancellationToken ct = default)
        {
            if (from.HasValue && to.HasValue && to < from) return BadRequest(new { error = "'to' must be after 'from'" });
            var report = await _svc.GetReportAsync(from, to, categoryIds is { Count: > 0 } ? categoryIds : null, branchId, marginThresholdPct, ct);
            var csv = MenuEngineeringCsv.Render(report);
            var name = $"menu-engineering-{(from?.ToString("yyyyMMdd") ?? "all")}-{(to?.ToString("yyyyMMdd") ?? "all")}.csv";
            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", name);
        }

        /// <summary>Ingredient cost breakdown of one item and the price that would hit ?targetMarginPct (default 70).</summary>
        [HttpGet("item/{id:int}/cost")]
        public async Task<IActionResult> ItemCost(int id, [FromQuery] decimal targetMarginPct = MenuEngineeringService.DefaultTargetMarginPct, CancellationToken ct = default)
        {
            var r = await _svc.GetItemCostBreakdownAsync(id, targetMarginPct, ct);
            return r is null ? NotFound(new { error = "Item not found" }) : Ok(r);
        }
    }
}
