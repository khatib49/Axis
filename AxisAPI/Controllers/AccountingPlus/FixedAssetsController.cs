using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.AccountingPlus
{
    [ApiController]
    [Route("api/accountingplus/assets")]
    [Authorize(Roles = "admin")]
    public class FixedAssetsController : ControllerBase
    {
        private readonly IFixedAssetService _svc;
        public FixedAssetsController(IFixedAssetService svc) => _svc = svc;

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
            => Ok(await _svc.ListAsync(status, ct));

        [HttpGet("summary")]
        public async Task<IActionResult> Summary(CancellationToken ct)
            => Ok(await _svc.GetRegisterSummaryAsync(ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var r = await _svc.GetAsync(id, ct);
            return r.Success ? Ok(r.Data) : NotFound(r);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] FixedAssetUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] FixedAssetUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteAsync(id, ct);
            return r.Success ? NoContent() : BadRequest(r);
        }

        [HttpGet("{id:int}/schedule")]
        public async Task<IActionResult> Schedule(int id, CancellationToken ct)
        {
            var r = await _svc.GetScheduleAsync(id, ct);
            return r.Success ? Ok(r.Data) : NotFound(r);
        }

        [HttpGet("{id:int}/entries")]
        public async Task<IActionResult> Entries(int id, CancellationToken ct)
            => Ok(await _svc.GetEntriesAsync(id, ct));

        /// <summary>Idempotent per asset/month. Optional assetId limits the run to one asset.</summary>
        [HttpPost("run-depreciation")]
        public async Task<IActionResult> RunDepreciation([FromQuery] int year, [FromQuery] int month, [FromQuery] int? assetId, CancellationToken ct)
        {
            var r = await _svc.RunDepreciationAsync(year, month, assetId, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpPost("{id:int}/dispose")]
        public async Task<IActionResult> Dispose(int id, [FromBody] AssetDisposeDto dto, CancellationToken ct)
        {
            var r = await _svc.DisposeAsync(id, dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }
    }
}
