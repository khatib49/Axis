using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Floor
{
    /// <summary>Floor plan: areas, tables, layout positions and the live layout for the till.</summary>
    [ApiController]
    [Route("api/floor")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class FloorController : ControllerBase
    {
        private readonly IFloorService _svc;
        public FloorController(IFloorService svc) => _svc = svc;

        // ── Areas ────────────────────────────────────────────────────
        [HttpGet("areas")]
        public async Task<IActionResult> ListAreas([FromQuery] bool includeInactive = false, CancellationToken ct = default)
            => Ok(await _svc.ListAreasAsync(includeInactive, ct));

        [HttpPost("areas")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateArea([FromBody] FloorAreaUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new BaseResponse<FloorAreaDto>(false, "Validation", "Name is required."));
            return Ok(await _svc.CreateAreaAsync(dto, ct));
        }

        [HttpPut("areas/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateArea(int id, [FromBody] FloorAreaUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAreaAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("areas/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteArea(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteAreaAsync(id, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        // ── Tables ───────────────────────────────────────────────────
        [HttpGet("tables")]
        public async Task<IActionResult> ListTables([FromQuery] int? areaId, [FromQuery] bool includeInactive = false, CancellationToken ct = default)
            => Ok(await _svc.ListTablesAsync(areaId, includeInactive, ct));

        [HttpPost("tables")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateTable([FromBody] DiningTableUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateTableAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("tables/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateTable(int id, [FromBody] DiningTableUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateTableAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("tables/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteTable(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteTableAsync(id, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        /// <summary>Bulk save of table positions/sizes from the layout editor.</summary>
        [HttpPost("tables/layout")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> SaveLayout([FromBody] BulkLayoutSaveDto dto, CancellationToken ct)
        {
            var r = await _svc.SaveLayoutAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        // ── Live layout (till) ───────────────────────────────────────
        [HttpGet("layout")]
        public async Task<IActionResult> Layout(CancellationToken ct) => Ok(await _svc.GetLayoutAsync(ct));
    }
}
