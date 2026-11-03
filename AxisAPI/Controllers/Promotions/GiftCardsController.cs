using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Promotions
{
    [ApiController]
    [Route("api/promotions/giftcards")]
    public class GiftCardsController : ControllerBase
    {
        private const string TillRoles = "admin,cashier,gamecashier,admin_fnb";
        private readonly IGiftCardService _svc;
        public GiftCardsController(IGiftCardService svc) => _svc = svc;

        // ── admin ────────────────────────────────────────────────────────
        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Issue([FromBody] GiftCardIssueDto dto, CancellationToken ct)
        {
            var r = await _svc.IssueAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpGet]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> List([FromQuery] GiftCardFilterDto filter, CancellationToken ct)
            => Ok(await _svc.ListAsync(filter, ct));

        [HttpGet("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var c = await _svc.GetAsync(id, ct);
            return c is null ? NotFound() : Ok(c);
        }

        [HttpGet("{id:int}/transactions")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Transactions(int id, CancellationToken ct)
            => Ok(await _svc.GetTransactionsAsync(id, ct));

        [HttpPost("{id:int}/disable")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Disable(int id, [FromBody] GiftCardAdjustDto? body, CancellationToken ct)
        {
            var r = await _svc.DisableAsync(id, body?.Note, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/adjust")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Adjust(int id, [FromBody] GiftCardAdjustDto dto, CancellationToken ct)
        {
            var r = await _svc.AdjustAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/load")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Load(int id, [FromBody] GiftCardLoadDto dto, CancellationToken ct)
        {
            var r = await _svc.LoadAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/refund")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Refund(int id, [FromBody] GiftCardRefundDto dto, CancellationToken ct)
        {
            var r = await _svc.RefundAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        // ── till ─────────────────────────────────────────────────────────
        [HttpGet("lookup/{code}")]
        [Authorize(Roles = TillRoles)]
        public async Task<IActionResult> Lookup(string code, CancellationToken ct)
        {
            var r = await _svc.LookupAsync(code, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        /// <summary>Applies part of the card balance AS PAYMENT on an open invoice.</summary>
        [HttpPost("redeem")]
        [Authorize(Roles = TillRoles)]
        public async Task<IActionResult> Redeem([FromBody] GiftCardRedeemDto dto, CancellationToken ct)
        {
            var r = await _svc.RedeemAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
