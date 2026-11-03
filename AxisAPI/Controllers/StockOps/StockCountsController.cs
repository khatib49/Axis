using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.StockOps
{
    [ApiController]
    [Route("api/stockops/counts")]
    [Authorize(Roles = "admin,stock,chef,admin_fnb")]
    public class StockCountsController : ControllerBase
    {
        private readonly IStockCountService _svc;
        public StockCountsController(IStockCountService svc) { _svc = svc; }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? locationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
            => Ok(await _svc.ListAsync(status, locationId, page, pageSize, ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var c = await _svc.GetAsync(id, ct);
            return c == null ? NotFound() : Ok(c);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] StockCountCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateDraftAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}/lines")]
        public async Task<IActionResult> UpdateLines(int id, [FromBody] StockCountLinesUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateLinesAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/post")]
        public async Task<IActionResult> Post(int id, CancellationToken ct)
        {
            var r = await _svc.PostAsync(id, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, CancellationToken ct)
        {
            var r = await _svc.CancelAsync(id, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpGet("{id:int}/variance")]
        public async Task<IActionResult> Variance(int id, [FromQuery] int top = 10, CancellationToken ct = default)
        {
            var r = await _svc.VarianceReportAsync(id, top, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
