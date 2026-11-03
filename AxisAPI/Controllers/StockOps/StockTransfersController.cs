using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.StockOps
{
    [ApiController]
    [Route("api/stockops/transfers")]
    [Authorize(Roles = "admin,stock,chef,admin_fnb")]
    public class StockTransfersController : ControllerBase
    {
        private readonly IStockTransferService _svc;
        public StockTransfersController(IStockTransferService svc) { _svc = svc; }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, [FromQuery] int? locationId, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
            => Ok(await _svc.ListAsync(status, locationId, page, pageSize, ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var t = await _svc.GetAsync(id, ct);
            return t == null ? NotFound() : Ok(t);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] StockTransferCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] StockTransferUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
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
    }
}
