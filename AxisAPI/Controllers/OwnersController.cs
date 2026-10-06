using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    /// <summary>
    /// Owners (partners + ownership %) and their drawings — cash taken out
    /// for personal use, booked against equity, never as an expense.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "admin")]
    public class OwnersController : ControllerBase
    {
        private readonly IOwnerService _svc;
        public OwnersController(IOwnerService svc) => _svc = svc;

        private int? CurrentUserId()
        {
            var raw = User?.FindFirst("sub")?.Value ?? User?.FindFirst("id")?.Value;
            return int.TryParse(raw, out var id) ? id : null;
        }

        // ── Owners ──────────────────────────────────────────────────────

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<OwnerDto>>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
            => Ok(await _svc.ListAsync(includeInactive, ct));

        [HttpGet("{id:int}")]
        public async Task<ActionResult<OwnerDto>> Get(int id, CancellationToken ct)
        {
            var dto = await _svc.GetAsync(id, ct);
            return dto is null ? NotFound() : Ok(dto);
        }

        [HttpPost]
        public async Task<ActionResult<OwnerDto>> Create([FromBody] OwnerCreateDto dto, CancellationToken ct)
        {
            try
            {
                var created = await _svc.CreateAsync(dto, CurrentUserId(), ct);
                return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<OwnerDto>> Update(int id, [FromBody] OwnerUpdateDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.UpdateAsync(id, dto, CurrentUserId(), ct)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        // Soft-delete (hide). The owner's account and drawings history stay.
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
            => await _svc.DeactivateAsync(id, ct) ? NoContent() : NotFound();

        // ── Drawings ────────────────────────────────────────────────────

        [HttpGet("drawings")]
        public async Task<ActionResult<PagedOwnerDrawingsResult>> QueryDrawings(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? ownerId,
            [FromQuery] bool includeVoided = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
            => Ok(await _svc.QueryDrawingsAsync(new OwnerDrawingFilter(from, to, ownerId, includeVoided, page, pageSize), ct));

        [HttpPost("drawings")]
        public async Task<ActionResult<OwnerDrawingDto>> CreateDrawing([FromBody] OwnerDrawingCreateDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.CreateDrawingAsync(dto, CurrentUserId(), ct)); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpPut("drawings/{id:int}")]
        public async Task<ActionResult<OwnerDrawingDto>> UpdateDrawing(int id, [FromBody] OwnerDrawingUpdateDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.UpdateDrawingAsync(id, dto, CurrentUserId(), ct)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        // Cancel: the row stays (marked cancelled) and its journal entry is voided.
        [HttpPost("drawings/{id:int}/void")]
        public async Task<ActionResult<OwnerDrawingDto>> VoidDrawing(int id, [FromBody] OwnerDrawingVoidDto? dto, CancellationToken ct)
        {
            try { return Ok(await _svc.VoidDrawingAsync(id, dto?.Reason, CurrentUserId(), ct)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        // ── Report ──────────────────────────────────────────────────────

        /// <summary>Per-owner drawings for the period with share %, entitlement and over/under-drawn.</summary>
        [HttpGet("drawings-summary")]
        public async Task<ActionResult<OwnerDrawingsSummaryDto>> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
            => Ok(await _svc.GetSummaryAsync(from, to, ct));
    }
}
