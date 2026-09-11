using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    /// <summary>
    /// Online payments — pay links, the public pay page, gateway callbacks
    /// and the admin ledger. Admin endpoints are [Authorize]; the public
    /// pay-page endpoints and the gateway callback are anonymous by design.
    /// </summary>
    [ApiController]
    [Route("api/payments")]
    // Till roles may CREATE pay links (send a top-up / invoice link to a
    // customer); everything else is admin-only via the per-action attribute
    // (ASP.NET ANDs class + method [Authorize]).
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class PaymentsController : ControllerBase
    {
        private readonly IOnlinePaymentService _svc;
        private readonly ILogger<PaymentsController> _logger;

        public PaymentsController(IOnlinePaymentService svc, ILogger<PaymentsController> logger)
        {
            _svc = svc;
            _logger = logger;
        }

        private string Actor => User?.Identity?.Name ?? "admin";

        // ── Admin ────────────────────────────────────────────────────────
        [Authorize(Roles = "admin")]
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] string? status, [FromQuery] string? purpose, [FromQuery] string? provider,
            [FromQuery] string? environment, [FromQuery] string? search,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
            => Ok(await _svc.ListAsync(new OnlinePaymentFilterDto(from, to, status, purpose, provider, environment, search, page, pageSize), ct));

        [Authorize(Roles = "admin")]
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var d = await _svc.GetAsync(id, ct);
            return d is null ? NotFound() : Ok(d);
        }

        /// <summary>Create a pay link (custom amount, wallet top-up, or open invoice).</summary>
        [HttpPost("links")]
        public async Task<IActionResult> CreateLink([FromBody] OnlinePaymentCreateDto body, CancellationToken ct)
        {
            try { return Ok(await _svc.CreateAsync(body, Actor, ct)); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        [Authorize(Roles = "admin")]
        [HttpPost("{id:int}/reconcile")]
        public async Task<IActionResult> Reconcile(int id, CancellationToken ct)
        {
            var d = await _svc.ReconcileAsync(id, Actor, ct);
            return d is null ? NotFound() : Ok(d);
        }

        [Authorize(Roles = "admin")]
        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, [FromBody] CancelBody? body, CancellationToken ct)
        {
            try { return await _svc.CancelAsync(id, Actor, body?.Reason, ct) ? NoContent() : NotFound(); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }
        public record CancelBody(string? Reason);

        /// <summary>What to hand to the gateway (callback URL) + configured state per environment.</summary>
        [Authorize(Roles = "admin")]
        [HttpGet("providers/{provider}/config")]
        public async Task<IActionResult> ProviderConfig(string provider, CancellationToken ct)
        {
            try { return Ok(await _svc.GetProviderConfigAsync(provider, ct)); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        // ── Public pay page ──────────────────────────────────────────────
        [AllowAnonymous]
        [HttpGet("public/{code}")]
        public async Task<IActionResult> Public(string code, CancellationToken ct)
        {
            var d = await _svc.GetPublicAsync(code, ct);
            return d is null ? NotFound(new { error = "This payment link does not exist." }) : Ok(d);
        }

        [AllowAnonymous]
        [HttpPost("public/{code}/start")]
        public async Task<IActionResult> Start(string code, CancellationToken ct)
            => Ok(await _svc.StartCheckoutAsync(code, ct));

        // ── Gateway callbacks ────────────────────────────────────────────
        /// <summary>
        /// MontyPay notification URL. Form-urlencoded body, signed with the
        /// merchant password. Always answers 200 — a non-2xx would trip the
        /// gateway's circuit breaker; verification failures are logged and
        /// ignored instead.
        /// </summary>
        [AllowAnonymous]
        [HttpPost("montypay/callback")]
        public async Task<IActionResult> MontyPayCallback(CancellationToken ct)
            => await CallbackAsync("MontyPay", ct);

        private async Task<IActionResult> CallbackAsync(string provider, CancellationToken ct)
        {
            var form = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string raw;
            try
            {
                Request.EnableBuffering();
                using (var reader = new StreamReader(Request.Body, leaveOpen: true))
                    raw = await reader.ReadToEndAsync(ct);
                Request.Body.Position = 0;

                if (Request.HasFormContentType)
                {
                    var f = await Request.ReadFormAsync(ct);
                    foreach (var kv in f) form[kv.Key] = kv.Value.ToString();
                }
                else if (!string.IsNullOrWhiteSpace(raw) && raw.TrimStart().StartsWith("{"))
                {
                    using var jd = System.Text.Json.JsonDocument.Parse(raw);
                    foreach (var prop in jd.RootElement.EnumerateObject())
                        form[prop.Name] = prop.Value.ValueKind == System.Text.Json.JsonValueKind.String
                            ? prop.Value.GetString() ?? ""
                            : prop.Value.ToString();
                }
                else if (!string.IsNullOrWhiteSpace(raw))
                {
                    // Some gateways post a form without the content-type header.
                    foreach (var kv in Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(raw))
                        form[kv.Key] = kv.Value.ToString();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Provider} callback body could not be read", provider);
                return Ok();
            }

            await _svc.HandleCallbackAsync(provider, form, raw, ct);
            return Ok();
        }
    }
}
