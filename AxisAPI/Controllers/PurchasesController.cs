using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "admin,chef,admin_fnb")]
    public class PurchasesController : ControllerBase
    {
        private readonly IPurchaseService _svc;
        private readonly IHttpContextAccessor _http;

        public PurchasesController(IPurchaseService svc, IHttpContextAccessor http)
        {
            _svc = svc;
            _http = http;
        }

        private string? Actor => _http.HttpContext?.User?.Identity?.Name;

        [HttpPost]
        public async Task<ActionResult<PurchaseDto>> Create([FromBody] PurchaseCreateDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.CreateAsync(dto, Actor, ct)); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<PurchaseDto>> Get(int id, CancellationToken ct)
        {
            var dto = await _svc.GetAsync(id, ct);
            return dto is null ? NotFound() : Ok(dto);
        }

        [HttpGet]
        public async Task<ActionResult<PaginatedResponse<PurchaseDto>>> List(
            [FromQuery] int? supplierId,
            [FromQuery] int? ingredientId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            CancellationToken ct = default)
        {
            var filter = new PurchaseFilterDto(supplierId, ingredientId, from, to, page, pageSize);
            return Ok(await _svc.ListAsync(filter, ct));
        }

        [HttpGet("price-trend/{ingredientId:int}")]
        public async Task<ActionResult<IReadOnlyList<PriceTrendPointDto>>> PriceTrend(
            int ingredientId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct = default)
            => Ok(await _svc.GetPriceTrendAsync(ingredientId, from, to, ct));
    }
}
