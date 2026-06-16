using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "admin,chef,admin_fnb")]
    public class IngredientsController : ControllerBase
    {
        private readonly IIngredientService _svc;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public IngredientsController(IIngredientService svc, IHttpContextAccessor httpContextAccessor)
        {
            _svc = svc;
            _httpContextAccessor = httpContextAccessor;
        }

        private string? Actor => _httpContextAccessor.HttpContext?.User?.Identity?.Name;

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<IngredientDto>>> List(
            [FromQuery] bool includeHidden = false,
            CancellationToken ct = default)
            => Ok(await _svc.ListAsync(includeHidden, ct));

        [HttpGet("low-stock")]
        public async Task<ActionResult<IReadOnlyList<IngredientDto>>> LowStock(CancellationToken ct)
            => Ok(await _svc.GetLowStockAsync(ct));

        [HttpGet("{id:int}")]
        public async Task<ActionResult<IngredientDto>> Get(int id, CancellationToken ct)
        {
            var dto = await _svc.GetAsync(id, ct);
            return dto is null ? NotFound() : Ok(dto);
        }

        [HttpPost]
        public async Task<ActionResult<IngredientDto>> Create([FromBody] IngredientCreateDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.CreateAsync(dto, Actor, ct)); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<IngredientDto>> Update(int id, [FromBody] IngredientUpdateDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.UpdateAsync(id, dto, Actor, ct)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
        {
            var ok = await _svc.DeactivateAsync(id, Actor, ct);
            return ok ? NoContent() : NotFound();
        }

        // ── Stock events ──────────────────────────────────────────────
        [HttpPost("add-stock")]
        public async Task<ActionResult<IngredientDto>> AddStock([FromBody] AddStockRequestDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.AddStockAsync(dto, Actor, ct)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPost("record-waste")]
        public async Task<ActionResult<IngredientDto>> RecordWaste([FromBody] RecordWasteRequestDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.RecordWasteAsync(dto, Actor, ct)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpPost("adjust-stock")]
        public async Task<ActionResult<IngredientDto>> AdjustStock([FromBody] AdjustStockRequestDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.AdjustStockAsync(dto, Actor, ct)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        // ── Audit log ──────────────────────────────────────────────────
        [HttpGet("movements")]
        public async Task<ActionResult<PaginatedResponse<StockMovementDto>>> Movements(
            [FromQuery] int? ingredientId,
            [FromQuery] string? type,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            var filter = new StockMovementFilterDto(ingredientId, type, from, to, page, pageSize);
            return Ok(await _svc.GetMovementsAsync(filter, ct));
        }
    }
}
