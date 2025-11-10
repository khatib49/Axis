using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ExpenseCategoryController : ControllerBase
    {
        private readonly IExpenseCategoryService _svc;
        public ExpenseCategoryController(IExpenseCategoryService svc) => _svc = svc;

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<ExpenseCategoryDto>>> List(CancellationToken ct)
            => Ok(await _svc.ListAsync(ct));

        [HttpPost]
        public async Task<ActionResult<ExpenseCategoryDto>> Create([FromBody] ExpenseCategoryCreateDto dto, CancellationToken ct)
        {
            var created = await _svc.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(List), new { id = created.Id }, created);
        }


        // PUT: api/expense-categories/{id}
        [HttpPut("{id:int}")]
        public async Task<ActionResult<ExpenseCategoryDto>> UpdateAsync([FromRoute] int id, [FromBody] ExpenseCategoryUpdateDto dto, CancellationToken ct)
        {
            var updated = await _svc.UpdateAsync(id, dto, ct);
            return Ok(updated);
        }

        // DELETE: api/expense-categories/{id}
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteAsync([FromRoute] int id, CancellationToken ct)
        {
            await _svc.DeleteAsync(id, ct);
            return Ok();
        }

    }
}
