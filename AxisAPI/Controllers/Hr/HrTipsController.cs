using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Hr
{
    [ApiController]
    [Route("api/hr/tips")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class HrTipsController : ControllerBase
    {
        private readonly IHrTipService _svc;
        public HrTipsController(IHrTipService svc) => _svc = svc;

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            var t = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var f = from ?? t.AddDays(-30);
            if (f > t) (f, t) = (t, f);
            return Ok(await _svc.ListAsync(f, t, ct));
        }

        [HttpGet("report")]
        public async Task<IActionResult> Report([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
        {
            var t = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var f = from ?? t;
            if (f > t) (f, t) = (t, f);
            return Ok(await _svc.ReportAsync(f, t, ct));
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var p = await _svc.GetAsync(id, ct);
            return p is null ? NotFound() : Ok(p);
        }

        [HttpPost]
        public async Task<IActionResult> Create(HrTipPoolCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, HrTipPoolUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            if (!r.Success && r.Error == "not_found") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/compute")]
        public async Task<IActionResult> Compute(int id, CancellationToken ct)
        {
            var r = await _svc.ComputeAsync(id, ct);
            if (!r.Success && r.Error == "not_found") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        /// <summary>Freezes the pool: allocations are final.</summary>
        [HttpPost("{id:int}/distribute")]
        public async Task<IActionResult> Distribute(int id, CancellationToken ct)
        {
            var r = await _svc.DistributeAsync(id, ct);
            if (!r.Success && r.Error == "not_found") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteAsync(id, ct) ? NoContent()
               : BadRequest(new BaseResponse<object>(false, "validation", "Pool not found or already distributed"));
    }
}
