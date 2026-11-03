using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.StockOps
{
    [ApiController]
    [Route("api/stockops/batches")]
    [Authorize(Roles = "admin,stock,chef,admin_fnb")]
    public class StockBatchesController : ControllerBase
    {
        private readonly IStockBatchService _svc;
        public StockBatchesController(IStockBatchService svc) { _svc = svc; }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] StockBatchFilterDto filter, CancellationToken ct)
            => Ok(await _svc.ListAsync(filter, ct));

        [HttpGet("expiring")]
        public async Task<IActionResult> Expiring([FromQuery] int days = 7, CancellationToken ct = default)
            => Ok(await _svc.GetExpiringAsync(days, ct));

        [HttpGet("expired")]
        public async Task<IActionResult> Expired(CancellationToken ct) => Ok(await _svc.GetExpiredAsync(ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var b = await _svc.GetAsync(id, ct);
            return b == null ? NotFound() : Ok(b);
        }

        // "Receive" = record a batch. Informational only: no stock movement
        // (the purchase that brought the goods in already moved stock).
        [HttpPost("receive")]
        public async Task<IActionResult> Receive([FromBody] StockBatchCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.ReceiveAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost]
        public Task<IActionResult> Create([FromBody] StockBatchCreateDto dto, CancellationToken ct) => Receive(dto, ct);

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] StockBatchUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();

        public record WriteOffRequest(string? Notes);

        [HttpPost("{id:int}/write-off")]
        public async Task<IActionResult> WriteOff(int id, [FromBody] WriteOffRequest? body, CancellationToken ct)
        {
            var r = await _svc.WriteOffExpiredAsync(id, body?.Notes, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
