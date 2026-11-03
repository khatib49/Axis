using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Promotions
{
    [ApiController]
    [Route("api/promotions/coupons")]
    public class CouponsController : ControllerBase
    {
        private const string TillRoles = "admin,cashier,gamecashier,admin_fnb";
        private readonly ICouponService _svc;
        public CouponsController(ICouponService svc) => _svc = svc;

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> List([FromQuery] CouponFilterDto filter, CancellationToken ct)
            => Ok(await _svc.ListAsync(filter, ct));

        [HttpGet("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var c = await _svc.GetAsync(id, ct);
            return c is null ? NotFound() : Ok(c);
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create([FromBody] CouponUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, [FromBody] CouponUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            return r.Success ? Ok(r) : (r.Error == "Not found" ? NotFound(r) : BadRequest(r));
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();

        [HttpGet("{id:int}/redemptions")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Redemptions(int id, CancellationToken ct)
            => Ok(await _svc.GetRedemptionsAsync(id, ct));

        /// <summary>Eligibility + computed discount; nothing is written.</summary>
        [HttpPost("validate")]
        [Authorize(Roles = TillRoles)]
        public async Task<IActionResult> Validate([FromBody] CouponValidateDto dto, CancellationToken ct)
        {
            var r = await _svc.ValidateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("redeem")]
        [Authorize(Roles = TillRoles)]
        public async Task<IActionResult> Redeem([FromBody] CouponRedeemDto dto, CancellationToken ct)
        {
            var r = await _svc.RedeemAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
