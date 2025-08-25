using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/room")]
    public class RoomController : ControllerBase
    {
        private readonly IRoomService _roomService;

        public RoomController(IRoomService roomService)
        {
            _roomService = roomService;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var room = await _roomService.GetAsync(id, ct);
            if (room is null) return NotFound();
            return Ok(room);
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var rooms = await _roomService.ListAsync(ct);
            return Ok(rooms);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, RoomUpdateDto dto, CancellationToken ct)
        {
            var success = await _roomService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> Create(RoomCreateDto dto, CancellationToken ct)
        {
            var created = await _roomService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var success = await _roomService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
