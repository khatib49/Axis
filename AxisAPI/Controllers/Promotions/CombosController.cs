using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Promotions
{
    [ApiController]
    [Route("api/promotions/combos")]
    public class CombosController : ControllerBase
    {
        private const string TillRoles = "admin,cashier,gamecashier,admin_fnb";
        private readonly IComboService _svc;
        public CombosController(IComboService svc) => _svc = svc;

        /// <summary>Till: active combos. Admin may pass includeInactive=true.</summary>
        [HttpGet]
        [Authorize(Roles = TillRoles)]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
        {
            if (includeInactive && !User.IsInRole("admin")) includeInactive = false;
            return Ok(await _svc.ListAsync(includeInactive, ct));
        }

        [HttpGet("{id:int}")]
        [Authorize(Roles = TillRoles)]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var r = await _svc.GetAsync(id, ct);
            return r is null ? NotFound() : Ok(r);
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create([FromBody] ComboUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, [FromBody] ComboUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            return r.Success ? Ok(r) : (r.Error == "Not found" ? NotFound(r) : BadRequest(r));
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();

        /// <summary>Till: add the combo to an open invoice, or open a new one.</summary>
        [HttpPost("sell")]
        [Authorize(Roles = TillRoles)]
        public async Task<IActionResult> Sell([FromBody] ComboSellDto dto, CancellationToken ct)
        {
            var r = await _svc.SellAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
