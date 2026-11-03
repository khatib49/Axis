using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Promotions
{
    [ApiController]
    [Route("api/promotions/happyhour")]
    public class HappyHourController : ControllerBase
    {
        private const string TillRoles = "admin,cashier,gamecashier,admin_fnb";
        private readonly IHappyHourService _svc;
        public HappyHourController(IHappyHourService svc) => _svc = svc;

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = true, CancellationToken ct = default)
            => Ok(await _svc.ListAsync(includeInactive, ct));

        [HttpGet("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var r = await _svc.GetAsync(id, ct);
            return r is null ? NotFound() : Ok(r);
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create([FromBody] HappyHourRuleUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, [FromBody] HappyHourRuleUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            return r.Success ? Ok(r) : (r.Error == "Not found" ? NotFound(r) : BadRequest(r));
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();

        /// <summary>Rules in effect now (or at ?at=UTC timestamp), evaluated in Asia/Beirut.</summary>
        [HttpGet("active-now")]
        [Authorize]
        public async Task<IActionResult> ActiveNow([FromQuery] DateTime? at, CancellationToken ct)
            => Ok(await _svc.ActiveNowAsync(at, ct));

        [HttpGet("price-preview")]
        [Authorize]
        public async Task<IActionResult> PricePreview([FromQuery] int itemId, [FromQuery] DateTime? at, CancellationToken ct)
        {
            var r = await _svc.PricePreviewAsync(itemId, at, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        [HttpPost("apply")]
        [Authorize(Roles = TillRoles)]
        public async Task<IActionResult> Apply([FromBody] HappyHourApplyDto dto, CancellationToken ct)
        {
            var r = await _svc.ApplyAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
