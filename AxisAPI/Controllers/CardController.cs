using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/card")]
    public class CardController : ControllerBase
    {
        private readonly ICardService _cardService;

        public CardController(ICardService cardService)
        {
            _cardService = cardService;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var card = await _cardService.GetAsync(id, ct);
            if (card is null) return NotFound();
            return Ok(card);
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var cards = await _cardService.ListAsync(pagination, ct);
            return Ok(cards);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, CardUpdateDto dto, CancellationToken ct)
        {
            var success = await _cardService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> Create(CardCreateDto dto, CancellationToken ct)
        {
            var created = await _cardService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var success = await _cardService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
