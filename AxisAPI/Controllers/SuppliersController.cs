using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "admin,chef,admin_fnb")]
    public class SuppliersController : ControllerBase
    {
        private readonly ISupplierService _svc;
        public SuppliersController(ISupplierService svc) => _svc = svc;

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<SupplierDto>>> List(
            [FromQuery] bool includeHidden = false, CancellationToken ct = default)
            => Ok(await _svc.ListAsync(includeHidden, ct));

        [HttpGet("{id:int}")]
        public async Task<ActionResult<SupplierDto>> Get(int id, CancellationToken ct)
        {
            var dto = await _svc.GetAsync(id, ct);
            return dto is null ? NotFound() : Ok(dto);
        }

        [HttpPost]
        public async Task<ActionResult<SupplierDto>> Create([FromBody] SupplierCreateDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.CreateAsync(dto, ct)); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<SupplierDto>> Update(int id, [FromBody] SupplierUpdateDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.UpdateAsync(id, dto, ct)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Deactivate(int id, CancellationToken ct)
        {
            var ok = await _svc.DeactivateAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }
    }
}
