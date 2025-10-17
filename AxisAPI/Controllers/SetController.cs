using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SetController : ControllerBase
    {
        private readonly ISetService _setService;

        public SetController(ISetService setService) => _setService= setService;

        [HttpGet("{id:int}")]
        public async Task<ActionResult<RoomSetDto>> Get(int id, CancellationToken ct)
        {
            var dto = await _setService.GetAsync(id, ct);
            return dto is null ? NotFound() : Ok(dto);
        }

        [HttpGet]
        public async Task<ActionResult<PaginatedResponse<RoomSetDto>>> List(
            [FromQuery] RoomSetListFilterDto f, CancellationToken ct)
        {
            var res = await _setService.ListAsync(f, ct);
            return Ok(res);
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<RoomSetDto>> Create(RoomSetCreateDto dto, CancellationToken ct)
        {
            var created = await _setService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, RoomSetUpdateDto dto, CancellationToken ct)
        {
            var ok = await _setService.UpdateAsync(id, dto, ct);
            return ok ? NoContent() : NotFound();
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var ok = await _setService.DeleteAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }
    }
}
