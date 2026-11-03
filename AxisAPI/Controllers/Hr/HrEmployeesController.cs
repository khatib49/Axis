using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Hr
{
    [ApiController]
    [Route("api/hr/employees")]
    public class HrEmployeesController : ControllerBase
    {
        private readonly IHrEmployeeService _svc;
        public HrEmployeesController(IHrEmployeeService svc) => _svc = svc;

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] bool includeInactive = false,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
            => Ok(await _svc.ListAsync(search, includeInactive, page, pageSize, ct));

        /// <summary>The caller's own employee profile (via user link). Any staff.</summary>
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me(CancellationToken ct)
        {
            var me = await _svc.GetMineAsync(ct);
            return me is null ? NotFound() : Ok(me);
        }

        [HttpGet("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var e = await _svc.GetAsync(id, ct);
            return e is null ? NotFound() : Ok(e);
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create(HrEmployeeCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, HrEmployeeUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            if (!r.Success && r.Error == "not_found") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();

        [HttpPost("{id:int}/pin")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> SetPin(int id, HrSetPinDto dto, CancellationToken ct)
        {
            var r = await _svc.SetPinAsync(id, dto.Pin, ct);
            if (!r.Success && r.Error == "not_found") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/link-user")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> LinkUser(int id, HrLinkUserDto dto, CancellationToken ct)
        {
            var r = await _svc.LinkUserAsync(id, dto.UserId, ct);
            if (!r.Success && r.Error == "not_found") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
