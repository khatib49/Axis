using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.StockOps
{
    [ApiController]
    [Route("api/stockops/locations")]
    [Authorize(Roles = "admin,stock,chef,admin_fnb")]
    public class StockLocationsController : ControllerBase
    {
        private readonly IStockLocationService _svc;
        public StockLocationsController(IStockLocationService svc) { _svc = svc; }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
            => Ok(await _svc.ListAsync(includeInactive, ct));

        [HttpGet("default")]
        public async Task<IActionResult> GetDefault(CancellationToken ct) => Ok(await _svc.GetOrCreateDefaultAsync(ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var l = await _svc.GetAsync(id, ct);
            return l == null ? NotFound() : Ok(l);
        }

        [HttpGet("{id:int}/stock")]
        public async Task<IActionResult> Stock(int id, [FromQuery] string? search, CancellationToken ct)
            => Ok(await _svc.GetStockAsync(id, search, ct));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] StockLocationUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] StockLocationUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
        {
            var r = await _svc.DeactivateAsync(id, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
