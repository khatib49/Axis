using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.StockOps
{
    /// <summary>
    /// Phase 3: per-branch stock positions and receiving a purchase at a location.
    /// </summary>
    [ApiController]
    [Route("api/stockops")]
    [Authorize(Roles = "admin,stock,chef,admin_fnb")]
    public class BranchStockController : ControllerBase
    {
        private readonly IBranchStockService _svc;
        public BranchStockController(IBranchStockService svc) { _svc = svc; }

        /// <summary>Stock held by one branch (?branchId=, else X-Branch-Id, else the shared scope).</summary>
        [HttpGet("branch-position")]
        public async Task<IActionResult> BranchPosition([FromQuery] int? branchId, CancellationToken ct)
            => Ok(await _svc.GetBranchPositionAsync(branchId, ct));

        /// <summary>All branches side by side.</summary>
        [HttpGet("positions")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Positions(CancellationToken ct)
            => Ok(await _svc.GetPositionsAsync(ct));

        /// <summary>Moves a purchase's quantities from Main to a location (posted transfer "PO-{purchaseId}"); idempotent.</summary>
        [HttpPost("receive-to-location")]
        public async Task<IActionResult> ReceiveToLocation([FromBody] ReceiveToLocationDto dto, CancellationToken ct)
        {
            var r = await _svc.ReceiveToLocationAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
