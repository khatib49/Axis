using Application.DTOs;
using Application.IServices;
using Application.Services;
using Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/discount")]
    public class DiscountController : ControllerBase
    {
        private readonly IDiscountService _discountService;

        public DiscountController(IDiscountService discountService)
        {
            _discountService = discountService;
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var discount = await _discountService.GetAsync(id, ct);
            if (discount is null) return NotFound();
            return Ok(discount);
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var discounts = await _discountService.ListAsync(pagination, ct);
            return Ok(discounts);
        }

        [HttpGet("type/{type}")]
        public async Task<IActionResult> GetCategoriesByType(string type, [FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var discounts = await _discountService.GetByTypeAsync(type, pagination, ct);
            return Ok(discounts);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, DiscountUpdateDto dto, CancellationToken ct)
        {
            var success = await _discountService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create(DiscountCreateDto dto, CancellationToken ct)
        {
            var created = await _discountService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [Authorize(Roles = "admin")]
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var success = await _discountService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
