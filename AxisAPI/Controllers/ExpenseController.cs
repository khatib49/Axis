using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ExpenseController : ControllerBase
    {
        private readonly IExpenseService _svc;
        public ExpenseController(IExpenseService svc) => _svc = svc;

        [HttpGet("{id:int}")]
        public async Task<ActionResult<ExpenseDto>> GetById(int id, CancellationToken ct)
        {
            var dto = await _svc.GetByIdAsync(id, ct);
            return dto is null ? NotFound() : Ok(dto);
        }

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<PagedExpensesResult>> Query(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? categoryId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            var result = await _svc.QueryAsync(new ExpenseFilter(from, to, categoryId, page, pageSize), ct);
            return Ok(result);
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<ExpenseDto>> Create([FromBody] ExpenseCreateDto dto, CancellationToken ct)
        {
            // If you have auth, get user id from claims; here we accept none:
            int? createdBy = null;
            var created = await _svc.CreateAsync(dto, createdBy, ct);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<ExpenseDto>> Update(int id, [FromBody] ExpenseUpdateDto dto, CancellationToken ct)
        {
            var updated = await _svc.UpdateAsync(id, dto, ct);
            return Ok(updated);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var ok = await _svc.DeleteAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }
    }
}
