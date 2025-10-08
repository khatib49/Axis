using Application.DTOs;
using Application.IServices;
using Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/usercard")]
    [ApiController]
    public class UserCardController : ControllerBase
    {
        private readonly IUserCardService _userCardService;
        public UserCardController(IUserCardService userCardService)
        {
            _userCardService = userCardService;
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var userCard = await _userCardService.GetAsync(id, ct);
            if (userCard is null) return NotFound();
            return Ok(userCard);
        }

        [HttpPost]
        public async Task<IActionResult> Assign(UserCardCreateDto dto, CancellationToken ct)
        {
            var created = await _userCardService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var usersCards = await _userCardService.ListAsync(pagination, ct);
            return Ok(usersCards);
        }
        
        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, UserCardUpdateDto dto, CancellationToken ct)
        {
            var success = await _userCardService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var success = await _userCardService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
