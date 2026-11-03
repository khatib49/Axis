using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Promotions
{
    /// <summary>
    /// Automatic promotions on an open invoice: happy hour + combos are
    /// evaluated against the current lines and only the difference is booked.
    /// </summary>
    [ApiController]
    [Route("api/promotions")]
    public class PromotionsAutoApplyController : ControllerBase
    {
        private const string TillRoles = "admin,cashier,gamecashier,admin_fnb";
        private readonly IPromotionAutoApplyService _svc;
        public PromotionsAutoApplyController(IPromotionAutoApplyService svc) => _svc = svc;

        /// <summary>Re-evaluate happy hour and combos on an open invoice (idempotent, safe to call repeatedly).</summary>
        [HttpPost("auto-apply")]
        [Authorize(Roles = TillRoles)]
        public async Task<IActionResult> AutoApply([FromBody] AutoApplyRequestDto dto, CancellationToken ct)
        {
            var r = await _svc.ApplyAsync(dto.TransactionId, null, ct);
            return r.Success ? Ok(r) : (r.Error == "Invalid invoice" ? NotFound(r) : BadRequest(r));
        }

        /// <summary>What is currently applied on an invoice (happy hour, combos, coupons).</summary>
        [HttpGet("applied/{transactionId:int}")]
        [Authorize(Roles = TillRoles)]
        public async Task<IActionResult> Applied(int transactionId, CancellationToken ct)
        {
            var r = await _svc.GetAppliedAsync(transactionId, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }
    }
}
