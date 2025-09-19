using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/passtype")]
    public class PassTypeController : ControllerBase
    {
        private readonly IPassTypeService _passTypeService;

        public PassTypeController(IPassTypeService passTypeService)
        {
            _passTypeService = passTypeService;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var passType = await _passTypeService.GetAsync(id, ct);
            if (passType is null) return NotFound();
            return Ok(passType);
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var rooms = await _passTypeService.ListAsync(pagination, ct);
            return Ok(rooms);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, PassTypeUpdateDto dto, CancellationToken ct)
        {
            var success = await _passTypeService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> Create(PassTypeCreateDto dto, CancellationToken ct)
        {
            var created = await _passTypeService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var success = await _passTypeService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
