using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    /// <summary>
    /// Admin-only stock operations that don't fit the CRUD endpoints on
    /// IngredientsController. Right now this is just the one-shot
    /// rebuild of historical Consumption costs after Bug#9's unit-conversion
    /// fix, but this is the sensible home for future audit / repair
    /// endpoints (rebuild QoH from movements, re-issue journal entries
    /// for COGS, etc.).
    /// </summary>
    [ApiController]
    [Route("api/stock")]
    [Authorize(Roles = "admin")]
    public class StockController : ControllerBase
    {
        private readonly IStockService _svc;
        private readonly IHttpContextAccessor _http;

        public StockController(IStockService svc, IHttpContextAccessor http)
        {
            _svc = svc;
            _http = http;
        }

        /// <summary>
        /// Rebuilds Consumption StockMovements in the given period using
        /// today's recipes + unit conversion. Set <c>dryRun=true</c> to
        /// preview the delta before committing. Default is dry-run so an
        /// accidental call can't damage the books.
        ///
        /// Query:
        ///   POST /api/stock/rebuild-consumption-costs?dryRun=true&from=2026-06-01&to=2026-07-08
        /// </summary>
        [HttpPost("rebuild-consumption-costs")]
        public async Task<ActionResult<RebuildConsumptionCostsResultDto>> RebuildConsumptionCosts(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] bool dryRun = true,
            [FromQuery] int detailLimit = 200,
            CancellationToken ct = default)
        {
            var actor = _http.HttpContext?.User?.Identity?.Name ?? "admin";
            var filter = new RebuildConsumptionCostsFilterDto(from, to, dryRun, detailLimit);

            var result = await _svc.RebuildConsumptionCostsAsync(filter, actor, ct);
            return Ok(result);
        }
    }
}
