using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ChannelController : ControllerBase
    {
        private readonly IChannelService _svc;
        public ChannelController(IChannelService svc) => _svc = svc;

        // Cashier + admin both read this. Admin can pass includeHidden=true to
        // see soft-deleted channels (for restoring); cashier never passes it
        // and only sees active channels.
        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ChannelDto>>> List(
            [FromQuery] bool includeHidden = false,
            CancellationToken ct = default)
        {
            // Only admin is allowed to peek at hidden channels.
            if (includeHidden && !(User?.IsInRole("admin") ?? false))
                includeHidden = false;

            return Ok(await _svc.ListAsync(includeHidden, ct));
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ChannelDto>> Get(int id, CancellationToken ct)
        {
            var dto = await _svc.GetAsync(id, ct);
            return dto is null ? NotFound() : Ok(dto);
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<ChannelDto>> Create([FromBody] ChannelCreateDto dto, CancellationToken ct)
        {
            try
            {
                var created = await _svc.CreateAsync(dto, ct);
                return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<ChannelDto>> Update(int id, [FromBody] ChannelUpdateDto dto, CancellationToken ct)
        {
            try
            {
                return Ok(await _svc.UpdateAsync(id, dto, ct));
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        // Soft-delete (hide). The row stays; transactions that point at it
        // still resolve. The cashier picker just no longer shows it.
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
        {
            var ok = await _svc.DeactivateAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }
    }
}
