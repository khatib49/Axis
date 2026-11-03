using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.TillControls
{
    [ApiController]
    [Route("api/till/void-requests")]
    public class VoidRequestsController : ControllerBase
    {
        private readonly IVoidRequestService _svc;
        public VoidRequestsController(IVoidRequestService svc) => _svc = svc;

        /// <summary>Cashier asks to void or refund a transaction. Auto-approves voids whose reason needs no approval.</summary>
        [HttpPost]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> Create([FromBody] VoidRequestCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpGet]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> List([FromQuery] VoidRequestFilterDto filter, CancellationToken ct)
            => Ok(await _svc.ListAsync(filter, ct));

        [HttpGet("pending")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Pending(CancellationToken ct) => Ok(await _svc.ListPendingAsync(ct));

        [HttpGet("{id:int}")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var r = await _svc.GetAsync(id, ct);
            return r is null ? NotFound() : Ok(r);
        }

        [HttpPost("{id:int}/approve")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Approve(int id, [FromBody] VoidDecisionDto? dto, CancellationToken ct)
        {
            var r = await _svc.ApproveAsync(id, dto ?? new VoidDecisionDto(null), ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/reject")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Reject(int id, [FromBody] VoidDecisionDto? dto, CancellationToken ct)
        {
            var r = await _svc.RejectAsync(id, dto ?? new VoidDecisionDto(null), ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
