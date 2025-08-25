using Application.DTOs;
using Application.IServices;
using Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/gamesession")]
    [ApiController]
    public class GameSessionController : ControllerBase
    {
        private readonly IGameSessionService _gameSessionService;
        public GameSessionController(IGameSessionService gameSessionService)
        {
            _gameSessionService = gameSessionService;
        }
        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var room = await _gameSessionService.GetAsync(id, ct);
            if (room is null) return NotFound();
            return Ok(room);
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var rooms = await _gameSessionService.ListAsync(ct);
            return Ok(rooms);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, GameSessionUpdateDto dto, CancellationToken ct)
        {
            var success = await _gameSessionService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> Create(GameSessionCreateDto dto, CancellationToken ct)
        {
            var created = await _gameSessionService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var success = await _gameSessionService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
