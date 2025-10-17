using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/room")]
    public class RoomController : ControllerBase
    {
        private readonly IRoomService _roomService;
        private readonly ITransactionRecordService _itransactionRecordService;

        public RoomController(IRoomService roomService, ITransactionRecordService itransactionRecordService)
        {
            _roomService = roomService;
            _itransactionRecordService = itransactionRecordService;
        }


        /// <summary>
        /// Get available and unavailable sets for a room.
        /// ongoingStatusId: the StatusId considered "in use" (defaults to 1).
        /// </summary>
        [HttpGet("{roomId:int}/sets/availability")]
        public async Task<ActionResult<RoomSetsAvailabilityDto>> GetRoomSetsAvailability(
            int roomId,
            [FromQuery] int ongoingStatusId = 1,
            CancellationToken ct = default)
        {
            var result = await _itransactionRecordService.GetRoomSetsAvailability(roomId, ongoingStatusId, ct);
            if (result is null) return NotFound($"Room {roomId} not found.");
            return Ok(result);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var room = await _roomService.GetAsync(id, ct);
            if (room is null) return NotFound();
            return Ok(room);
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var rooms = await _roomService.ListAsync(pagination, ct);
            return Ok(rooms);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, RoomUpdateDto dto, CancellationToken ct)
        {
            var success = await _roomService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create(RoomCreateDto dto, CancellationToken ct)
        {
            var created = await _roomService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var success = await _roomService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
