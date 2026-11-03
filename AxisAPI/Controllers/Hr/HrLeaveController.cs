using Application.Common;
using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Hr
{
    [ApiController]
    [Route("api/hr/leave")]
    [Authorize]
    public class HrLeaveController : ControllerBase
    {
        private readonly IHrLeaveService _svc;
        private readonly ICurrentUserAccessor _user;
        public HrLeaveController(IHrLeaveService svc, ICurrentUserAccessor user) { _svc = svc; _user = user; }

        private bool IsAdmin => _user.IsInRole("admin");

        // ── Types ─────────────────────────────────────────────────────────
        [HttpGet("types")]
        public async Task<IActionResult> Types(CancellationToken ct) => Ok(await _svc.ListTypesAsync(ct));

        [HttpPost("types")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateType(HrLeaveTypeUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new BaseResponse<object>(false, "validation", "Name is required"));
            return Ok(await _svc.CreateTypeAsync(dto, ct));
        }

        [HttpPut("types/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateType(int id, HrLeaveTypeUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new BaseResponse<object>(false, "validation", "Name is required"));
            return await _svc.UpdateTypeAsync(id, dto, ct) ? NoContent() : NotFound();
        }

        [HttpDelete("types/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteType(int id, CancellationToken ct)
            => await _svc.DeleteTypeAsync(id, ct) ? NoContent()
               : BadRequest(new BaseResponse<object>(false, "validation", "Leave type not found or still used by requests"));

        // ── Requests ──────────────────────────────────────────────────────
        [HttpGet("requests")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Requests([FromQuery] string? status, [FromQuery] int? employeeId, [FromQuery] int? year, CancellationToken ct)
            => Ok(await _svc.ListRequestsAsync(status, employeeId, year, ct));

        [HttpGet("requests/mine")]
        public async Task<IActionResult> MyRequests(CancellationToken ct) => Ok(await _svc.ListMyRequestsAsync(ct));

        /// <summary>Self (via user link) or admin on behalf of an employee (EmployeeId set).</summary>
        [HttpPost("requests")]
        public async Task<IActionResult> Create(HrLeaveRequestCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateRequestAsync(dto, IsAdmin, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("requests/{id:int}/approve")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Approve(int id, HrLeaveDecisionDto? dto, CancellationToken ct)
        {
            var r = await _svc.DecideAsync(id, true, dto?.Note, ct);
            if (!r.Success && r.Error == "not_found") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("requests/{id:int}/reject")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Reject(int id, HrLeaveDecisionDto? dto, CancellationToken ct)
        {
            var r = await _svc.DecideAsync(id, false, dto?.Note, ct);
            if (!r.Success && r.Error == "not_found") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("requests/{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, CancellationToken ct)
        {
            var r = await _svc.CancelAsync(id, IsAdmin, ct);
            if (!r.Success && r.Error == "not_found") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        // ── Balances ──────────────────────────────────────────────────────
        [HttpGet("balances")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Balances([FromQuery] int? year, CancellationToken ct)
            => Ok(await _svc.AllBalancesAsync(year ?? DateTime.UtcNow.Year, ct));

        [HttpGet("balances/{employeeId:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Balance(int employeeId, [FromQuery] int? year, CancellationToken ct)
        {
            var b = await _svc.BalanceAsync(employeeId, year ?? DateTime.UtcNow.Year, ct);
            return b is null ? NotFound() : Ok(b);
        }
    }
}
