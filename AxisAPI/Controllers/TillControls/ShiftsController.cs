using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.TillControls
{
    [ApiController]
    [Route("api/till/shifts")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class ShiftsController : ControllerBase
    {
        private readonly IShiftService _shifts;
        public ShiftsController(IShiftService shifts) => _shifts = shifts;

        /// <summary>Open a shift on a register with an opening float.</summary>
        [HttpPost("open")]
        public async Task<IActionResult> Open([FromBody] OpenShiftDto dto, CancellationToken ct)
        {
            var r = await _shifts.OpenAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        /// <summary>The caller's open shift (204 when none).</summary>
        [HttpGet("current")]
        public async Task<IActionResult> Current([FromQuery] string? register, CancellationToken ct)
        {
            var s = await _shifts.GetCurrentAsync(register, ct);
            return s is null ? NoContent() : Ok(s);
        }

        [HttpPost("pay-in")]
        public async Task<IActionResult> PayIn([FromBody] CashMovementCreateDto dto, [FromQuery] int? shiftId, CancellationToken ct)
        {
            var r = await _shifts.PayInAsync(dto, shiftId, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("pay-out")]
        public async Task<IActionResult> PayOut([FromBody] CashMovementCreateDto dto, [FromQuery] int? shiftId, CancellationToken ct)
        {
            var r = await _shifts.PayOutAsync(dto, shiftId, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        /// <summary>Live X report for the caller's open shift (or a given open shift).</summary>
        [HttpGet("x-report")]
        public async Task<IActionResult> XReport([FromQuery] int? shiftId, CancellationToken ct)
        {
            var r = await _shifts.XReportAsync(shiftId, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        /// <summary>Close the shift with the counted cash; returns the frozen Z report.</summary>
        [HttpPost("close")]
        public async Task<IActionResult> Close([FromBody] CloseShiftDto dto, [FromQuery] int? shiftId, CancellationToken ct)
        {
            var r = await _shifts.CloseAsync(dto, shiftId, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> List([FromQuery] ShiftFilterDto filter, CancellationToken ct)
            => Ok(await _shifts.ListAsync(filter, ct));

        [HttpGet("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var s = await _shifts.GetAsync(id, ct);
            return s is null ? NotFound() : Ok(s);
        }

        /// <summary>Z report of a closed shift (frozen at close); X report when still open.</summary>
        [HttpGet("{id:int}/z-report")]
        public async Task<IActionResult> ZReport(int id, CancellationToken ct)
        {
            var r = await _shifts.ZReportAsync(id, ct);
            return r is null ? NotFound() : Ok(r);
        }
    }
}
