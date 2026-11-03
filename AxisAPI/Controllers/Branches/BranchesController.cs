using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Branches
{
    [ApiController]
    [Route("api/branches")]
    public class BranchesController : ControllerBase
    {
        private readonly IBranchService _svc;
        public BranchesController(IBranchService svc) => _svc = svc;

        /// <summary>Branches the caller may work in (admin → all; assigned; none → default branch).</summary>
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me(CancellationToken ct) => Ok(await _svc.GetMineAsync(ct));

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = true, CancellationToken ct = default) =>
            Ok(await _svc.ListAsync(includeInactive, ct));

        [HttpGet("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var b = await _svc.GetAsync(id, ct);
            return b is null ? NotFound() : Ok(b);
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create([FromBody] BranchCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? CreatedAtAction(nameof(Get), new { id = r.Data!.Id }, r.Data) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, [FromBody] BranchUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            if (r.Success) return Ok(r.Data);
            return r.Message == "Not found" ? NotFound(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteAsync(id, ct);
            if (r.Success) return NoContent();
            return r.Message == "Not found" ? NotFound(r) : BadRequest(r);
        }

        // ── Users ────────────────────────────────────────────────────────
        [HttpGet("{id:int}/users")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Users(int id, CancellationToken ct) => Ok(await _svc.ListUsersAsync(id, ct));

        [HttpPost("{id:int}/users")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> AssignUser(int id, [FromBody] AssignUserRequest req, CancellationToken ct)
        {
            var r = await _svc.AssignUserAsync(id, req, ct);
            if (r.Success) return Ok(r.Data);
            return r.Message == "Not found" ? NotFound(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}/users/{userId:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UnassignUser(int id, int userId, CancellationToken ct)
        {
            var r = await _svc.UnassignUserAsync(id, userId, ct);
            return r.Success ? NoContent() : NotFound(r);
        }

        // ── Settings ─────────────────────────────────────────────────────
        [HttpGet("{id:int}/settings")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Settings(int id, CancellationToken ct) => Ok(await _svc.GetSettingsAsync(id, ct));

        [HttpPut("{id:int}/settings")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> SetSettings(int id, [FromBody] List<BranchSettingUpsertDto> settings, CancellationToken ct)
        {
            var r = await _svc.SetSettingsAsync(id, settings ?? new(), ct);
            if (r.Success) return Ok(r.Data);
            return r.Message == "Not found" ? NotFound(r) : BadRequest(r);
        }

        // ── Consolidated dashboard ───────────────────────────────────────
        [HttpGet("consolidated")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Consolidated([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct) =>
            Ok(await _svc.GetConsolidatedAsync(from, to, ct));
    }
}
