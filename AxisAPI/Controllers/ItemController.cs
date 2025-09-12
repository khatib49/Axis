using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/item")]
    public class ItemController : ControllerBase
    {
        private readonly IItemService _itemService;

        public ItemController(IItemService itemService)
        {
            _itemService = itemService;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var item = await _itemService.GetAsync(id, ct);
            if (item is null) return NotFound();
            return Ok(item);
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var items = await _itemService.ListAsync(ct);
            return Ok(items);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, ItemUpdateDto dto, CancellationToken ct)
        {
            var success = await _itemService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> Create(ItemCreateDto dto, CancellationToken ct)
        {
            var created = await _itemService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var success = await _itemService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
