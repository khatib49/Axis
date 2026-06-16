using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/inventory")]
    [Authorize(Roles = "admin,chef,admin_fnb")]
    public class InventoryValuationController : ControllerBase
    {
        private readonly IInventoryValuationService _svc;
        public InventoryValuationController(IInventoryValuationService svc) => _svc = svc;

        /// <summary>
        /// Returns total inventory value + per-ingredient breakdown + top
        /// movers and slow movers for the given period (defaults to last
        /// 30 days when from/to are omitted).
        /// </summary>
        [HttpGet("valuation")]
        public async Task<ActionResult<InventoryValuationDto>> Valuation(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct = default)
            => Ok(await _svc.GetAsync(from, to, ct));
    }
}
