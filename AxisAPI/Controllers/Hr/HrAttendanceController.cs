using System.Text;
using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Hr
{
    [ApiController]
    [Route("api/hr/attendance")]
    public class HrAttendanceController : ControllerBase
    {
        private readonly IHrAttendanceService _svc;
        public HrAttendanceController(IHrAttendanceService svc) => _svc = svc;

        /// <summary>Till kiosk: PIN toggles clock-in / clock-out. Any logged-in till device.</summary>
        [HttpPost("clock")]
        [Authorize]
        public async Task<IActionResult> Clock(HrClockRequestDto dto, CancellationToken ct)
        {
            var r = await _svc.ClockAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? employeeId, CancellationToken ct)
        {
            var (f, t) = Window(from, to);
            return Ok(await _svc.ListAsync(f, t, employeeId, ct));
        }

        [HttpGet("daily")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Daily([FromQuery] DateOnly? date, CancellationToken ct)
            => Ok(await _svc.DailySummaryAsync(date ?? DateOnly.FromDateTime(DateTime.UtcNow), ct));

        [HttpGet("timesheet")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Timesheet([FromQuery] int employeeId, [FromQuery] int year, [FromQuery] int month, CancellationToken ct)
        {
            if (month < 1 || month > 12 || year < 2000) return BadRequest(new BaseResponse<object>(false, "validation", "Invalid year/month"));
            var ts = await _svc.TimesheetAsync(employeeId, year, month, ct);
            return ts is null ? NotFound() : Ok(ts);
        }

        /// <summary>Payroll CSV: employee,date,in,out,minutes,hourlyRate,amount.</summary>
        [HttpGet("export")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Export([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int? employeeId, CancellationToken ct)
        {
            var (f, t) = Window(from, to);
            var csv = await _svc.ExportCsvAsync(f, t, employeeId, ct);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"attendance_{f:yyyyMMdd}_{t:yyyyMMdd}.csv");
        }

        [HttpPost("manual")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> AddManual(HrAttendanceManualDto dto, CancellationToken ct)
        {
            var r = await _svc.AddManualAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, HrAttendanceUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            if (!r.Success && r.Error == "not_found") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();

        private static (DateOnly, DateOnly) Window(DateOnly? from, DateOnly? to)
        {
            var t = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var f = from ?? t.AddDays(-6);
            if (f > t) (f, t) = (t, f);
            return (f, t);
        }
    }
}
