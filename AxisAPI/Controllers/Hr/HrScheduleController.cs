using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Hr
{
    [ApiController]
    [Route("api/hr/schedule")]
    [Authorize]
    public class HrScheduleController : ControllerBase
    {
        private readonly IHrScheduleService _svc;
        public HrScheduleController(IHrScheduleService svc) => _svc = svc;

        // ── Templates ─────────────────────────────────────────────────────
        [HttpGet("templates")]
        public async Task<IActionResult> Templates(CancellationToken ct) => Ok(await _svc.ListTemplatesAsync(ct));

        [HttpPost("templates")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateTemplate(HrShiftTemplateUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new BaseResponse<object>(false, "validation", "Name is required"));
            return Ok(await _svc.CreateTemplateAsync(dto, ct));
        }

        [HttpPut("templates/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateTemplate(int id, HrShiftTemplateUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new BaseResponse<object>(false, "validation", "Name is required"));
            return await _svc.UpdateTemplateAsync(id, dto, ct) ? NoContent() : NotFound();
        }

        [HttpDelete("templates/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteTemplate(int id, CancellationToken ct)
            => await _svc.DeleteTemplateAsync(id, ct) ? NoContent() : NotFound();

        // ── Week views ────────────────────────────────────────────────────
        /// <summary>Employees × 7 days starting on the Monday of the week containing weekStart (default: this week).</summary>
        [HttpGet("week")]
        public async Task<IActionResult> Week([FromQuery] DateOnly? weekStart, CancellationToken ct)
            => Ok(await _svc.GetWeekAsync(weekStart, ct));

        [HttpGet("my-week")]
        public async Task<IActionResult> MyWeek([FromQuery] DateOnly? weekStart, CancellationToken ct)
        {
            var w = await _svc.GetMyWeekAsync(weekStart, ct);
            return w is null ? NotFound(new BaseResponse<object>(false, "not_linked", "Your login is not linked to an employee profile")) : Ok(w);
        }

        // ── Shifts ────────────────────────────────────────────────────────
        [HttpPost("shifts")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateShift(HrShiftCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateShiftAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("shifts/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateShift(int id, HrShiftUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateShiftAsync(id, dto, ct);
            if (!r.Success && r.Error == "not_found") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("shifts/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteShift(int id, CancellationToken ct)
            => await _svc.DeleteShiftAsync(id, ct) ? NoContent() : NotFound();

        [HttpPost("copy-previous-week")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CopyPreviousWeek(HrCopyWeekDto dto, CancellationToken ct)
        {
            var r = await _svc.CopyPreviousWeekAsync(dto.WeekStart, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
