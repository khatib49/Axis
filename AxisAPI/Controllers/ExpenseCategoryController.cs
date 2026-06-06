using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ExpenseCategoryController : ControllerBase
    {
        private readonly IExpenseCategoryService _svc;
        private readonly IBackfillService _backfill;
        public ExpenseCategoryController(IExpenseCategoryService svc, IBackfillService backfill)
        {
            _svc = svc;
            _backfill = backfill;
        }

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<IReadOnlyList<ExpenseCategoryDto>>> List(CancellationToken ct)
            => Ok(await _svc.ListAsync(ct));

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<ExpenseCategoryDto>> Create([FromBody] ExpenseCategoryCreateDto dto, CancellationToken ct)
        {
            var created = await _svc.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(List), new { id = created.Id }, created);
        }


        // PUT: api/expense-categories/{id}
        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<ExpenseCategoryDto>> UpdateAsync([FromRoute] int id, [FromBody] ExpenseCategoryUpdateDto dto, CancellationToken ct)
        {
            var updated = await _svc.UpdateAsync(id, dto, ct);
            return Ok(updated);
        }

        // DELETE: api/expense-categories/{id}
        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteAsync([FromRoute] int id, CancellationToken ct)
        {
            await _svc.DeleteAsync(id, ct);
            return Ok();
        }

        // POST: api/expensecategory/{id}/rebuild
        // Runs the per-category backfill SYNCHRONOUSLY and returns the result.
        // Lets the admin click "Rebuild Balances" in the UI and see immediately
        // how many expenses moved, were created, skipped, or failed — useful
        // when Save-via-Hangfire reports "Succeeded" but nothing seems to
        // change (typically because the category had zero expenses).
        [HttpPost("{id:int}/rebuild")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<BackfillResultDto>> Rebuild([FromRoute] int id, CancellationToken ct)
        {
            var result = await _backfill.BackfillCategoryAsync(id, ct);
            return Ok(result);
        }

    }
}
