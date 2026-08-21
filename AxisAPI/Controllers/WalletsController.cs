using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    /// <summary>
    /// Customer wallets. Reads and top-ups are open to every till role;
    /// taking money OUT (refund/adjust) and bonus tiers are admin-only.
    /// </summary>
    [ApiController]
    [Route("api/wallets")]
    public class WalletsController : ControllerBase
    {
        private readonly IWalletService _svc;
        private readonly IHttpContextAccessor _http;

        public WalletsController(IWalletService svc, IHttpContextAccessor http)
        {
            _svc = svc;
            _http = http;
        }

        private string Actor => _http.HttpContext?.User?.Identity?.Name ?? "system";

        [HttpGet("{userId:int}")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> GetSummary(int userId, [FromQuery] int recent = 10, CancellationToken ct = default)
            => Ok(await _svc.GetSummaryAsync(userId, recent, ct));

        /// <summary>Balances for a set of users in one call (clients table).</summary>
        [HttpPost("balances")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> GetBalances([FromBody] List<int> userIds, CancellationToken ct)
            => Ok(await _svc.GetBalancesAsync(userIds ?? new List<int>(), ct));

        [HttpGet("{userId:int}/transactions")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> GetHistory(int userId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
            => Ok(await _svc.GetHistoryAsync(userId, page, pageSize, ct));

        [HttpPost("{userId:int}/topup")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> TopUp(int userId, [FromBody] WalletTopUpRequestDto body, CancellationToken ct)
        {
            try
            {
                var res = await _svc.TopUpAsync(userId, body, Actor, ct);
                return res.Success ? Ok(res) : BadRequest(res);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        /// <summary>Admin-only: cash refund or balance correction. Negative delta removes credit.</summary>
        [HttpPost("{userId:int}/adjust")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Adjust(int userId, [FromBody] WalletAdjustRequestDto body, CancellationToken ct)
        {
            try
            {
                var res = await _svc.AdjustAsync(userId, body, Actor, ct);
                return res.Success ? Ok(res) : BadRequest(res);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // ── Bonus tiers ──────────────────────────────────────────────────
        // GET is till-wide so the top-up modal can preview the bonus.

        [HttpGet("bonus-tiers")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> GetTiers([FromQuery] bool includeInactive = false, CancellationToken ct = default)
            => Ok(await _svc.GetTiersAsync(includeInactive && User.IsInRole("admin"), ct));

        [HttpPost("bonus-tiers")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateTier([FromBody] WalletBonusTierUpsertDto body, CancellationToken ct)
        {
            try { return Ok(await _svc.CreateTierAsync(body, ct)); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPut("bonus-tiers/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateTier(int id, [FromBody] WalletBonusTierUpsertDto body, CancellationToken ct)
        {
            try { return await _svc.UpdateTierAsync(id, body, ct) ? NoContent() : NotFound(); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpDelete("bonus-tiers/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteTier(int id, CancellationToken ct)
            => await _svc.DeleteTierAsync(id, ct) ? NoContent() : NotFound();
    }
}
