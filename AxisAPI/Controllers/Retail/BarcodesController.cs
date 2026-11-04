using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Retail
{
    /// <summary>
    /// Item barcodes. Reads, lookup and labels are open to every till role;
    /// creating / changing codes is admin-only (action-level attribute).
    /// </summary>
    [ApiController]
    [Route("api/retail/barcodes")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class BarcodesController : ControllerBase
    {
        private readonly IBarcodeService _svc;
        public BarcodesController(IBarcodeService svc) { _svc = svc; }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int? itemId, [FromQuery] string? search, CancellationToken ct)
            => Ok(await _svc.ListAsync(itemId, search, ct));

        /// <summary>Till scan. Whitespace is stripped; variant codes resolve before item codes.</summary>
        [HttpGet("lookup/{code}")]
        public async Task<IActionResult> Lookup(string code, CancellationToken ct)
        {
            var r = await _svc.LookupAsync(code, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        /// <summary>Label data for the printable page: ?itemIds=1,2,3 (or repeated itemIds=1&amp;itemIds=2).</summary>
        [HttpGet("labels")]
        public async Task<IActionResult> Labels([FromQuery] string? itemIds, CancellationToken ct)
        {
            var ids = (itemIds ?? string.Empty)
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s, out var n) ? n : 0)
                .Where(n => n > 0);
            return Ok(await _svc.LabelsAsync(ids, ct));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var b = await _svc.GetAsync(id, ct);
            return b == null ? NotFound() : Ok(b);
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create([FromBody] BarcodeCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("generate")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Generate([FromBody] BarcodeGenerateDto dto, CancellationToken ct)
        {
            var r = await _svc.GenerateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, [FromBody] BarcodeUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/primary")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> SetPrimary(int id, CancellationToken ct)
        {
            var r = await _svc.SetPrimaryAsync(id, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteAsync(id, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
