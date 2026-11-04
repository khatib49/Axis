using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Promotions
{
    /// <summary>
    /// Phase 3: ledger reports for gift cards and promotion discounts, plus the
    /// repost / backfill actions for postings deferred by a locked period.
    /// </summary>
    [ApiController]
    [Route("api/promotions/accounting")]
    [Authorize(Roles = "admin")]
    public class PromotionAccountingController : ControllerBase
    {
        private readonly IPromotionAccountingService _svc;
        public PromotionAccountingController(IPromotionAccountingService svc) => _svc = svc;

        /// <summary>Creates 2150 / 4950 if missing and returns the accounts the module posts to.</summary>
        [HttpGet("accounts")]
        public async Task<IActionResult> Accounts(CancellationToken ct)
        {
            var r = await _svc.EnsureAccountsAsync(ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpGet("gift-card-liability")]
        public async Task<IActionResult> GiftCardLiability(CancellationToken ct)
        {
            var r = await _svc.GetGiftCardLiabilityReportAsync(ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpGet("discounts")]
        public async Task<IActionResult> Discounts([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        {
            var r = await _svc.GetDiscountsReportAsync(from, to, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpGet("postings")]
        public async Task<IActionResult> Postings([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] string? status, CancellationToken ct)
            => Ok(await _svc.GetPostingsAsync(from, to, status, ct));

        [HttpGet("pending")]
        public async Task<IActionResult> Pending(CancellationToken ct)
            => Ok(await _svc.GetPendingAsync(ct));

        [HttpPost("repost")]
        public async Task<IActionResult> Repost(CancellationToken ct)
        {
            var r = await _svc.RepostPendingAsync(ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("backfill")]
        public async Task<IActionResult> Backfill(CancellationToken ct)
        {
            var r = await _svc.BackfillAsync(ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
