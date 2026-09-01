using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
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

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var item = await _itemService.GetAsync(id, ct);
            if (item is null) return NotFound();
            return Ok(item);
        }

        [HttpGet("category/{categoryId:int}")]
        public async Task<IActionResult> GetItemsByCategoryId(int categoryId, [FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var items = await _itemService.GetByCategoryIdAsync(categoryId, pagination, ct);
            return Ok(items);
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var items = await _itemService.ListAsync(pagination, ct);
            return Ok(items);
        }

        /// <summary>All add-ons of an item, inactive included — feeds the admin editor.</summary>
        [HttpGet("{id:int}/addons")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> GetAddOns(int id, CancellationToken ct)
            => Ok(await _itemService.GetAddOnsAsync(id, ct));

        /// <summary>Replace-all sync of an item's add-ons from the admin editor.</summary>
        [HttpPut("{id:int}/addons")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> SetAddOns(int id, [FromBody] List<ItemAddOnUpsertDto> body, CancellationToken ct)
        {
            try { return Ok(await _itemService.SetAddOnsAsync(id, body, ct)); }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, [FromForm] ItemUpdateDto dto, CancellationToken ct)
        {
            var success = await _itemService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create([FromForm]ItemCreateDto dto, CancellationToken ct)
        {
            var created = await _itemService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var success = await _itemService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
