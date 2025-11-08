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
    }
}
