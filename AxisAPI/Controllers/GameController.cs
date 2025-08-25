using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/game")]
    public class GameController : ControllerBase
    {
        private readonly IGameService _gameService;

        public GameController(IGameService gameService)
        {
            _gameService = gameService;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var game = await _gameService.GetAsync(id, ct);
            if (game is null) return NotFound();
            return Ok(game);
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var games = await _gameService.ListAsync(ct);
            return Ok(games);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, GameUpdateDto dto, CancellationToken ct)
        {
            var success = await _gameService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> Create(GameCreateDto dto, CancellationToken ct)
        {
            var created = await _gameService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var success = await _gameService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
