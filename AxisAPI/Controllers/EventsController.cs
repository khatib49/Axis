using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.DTOs;
using Application.IServices;
using Application.Services.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    /// <summary>
    /// PUBLIC endpoints for the event landing page — anonymous by design so
    /// anyone with the link can register. Only reads config and writes a
    /// single registration row; no business data is exposed.
    /// </summary>
    [ApiController]
    [Route("api/events")]
    [AllowAnonymous]
    public class EventsController : ControllerBase
    {
        private readonly IEventRegistrationService _svc;
        private readonly IEventService _events;
        private readonly IIntegrationSettingsService _settings;
        private readonly WhishGateway _whish;
        private readonly ILogger<EventsController> _logger;

        public EventsController(
            IEventRegistrationService svc,
            IEventService events,
            IIntegrationSettingsService settings,
            WhishGateway whish,
            ILogger<EventsController> logger)
        {
            _svc = svc;
            _events = events;
            _settings = settings;
            _whish = whish;
            _logger = logger;
        }

        /// <summary>Price, currency, WhatsApp number and which gateways are live.</summary>
        [HttpGet("{eventKey}/config")]
        public async Task<IActionResult> Config(string eventKey, CancellationToken ct)
            => Ok(await _svc.GetPublicConfigAsync(eventKey, ct));

        /// <summary>
        /// The whole published event — title, copy, features, media, price
        /// and which payment methods to render. 404 when the event doesn't
        /// exist or hasn't been published yet.
        /// </summary>
        [HttpGet("{eventKey}")]
        public async Task<IActionResult> PublicEvent(string eventKey, CancellationToken ct)
        {
            var e = await _events.GetPublicAsync(eventKey, ct);
            return e is null ? NotFound(new { message = "Event not found." }) : Ok(e);
        }

        /// <summary>Anonymous registration. Returns a redirect URL for Visa/Whish.</summary>
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] EventRegisterRequestDto dto, CancellationToken ct)
        {
            try
            {
                return Ok(await _svc.RegisterAsync(dto, ct));
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event registration failed");
                return StatusCode(500, new { message = "Registration failed. Please try again." });
            }
        }

        /// <summary>
        /// Whish redirects the buyer here after payment. We NEVER trust the
        /// redirect alone — we re-ask Whish for the collect status before
        /// marking the row paid, then bounce the buyer to the thank-you page.
        /// </summary>
        [HttpGet("whish/return")]
        public async Task<IActionResult> WhishReturn(
            [FromQuery] string? externalId,
            [FromQuery] int? reg,
            [FromQuery] string? ev,
            CancellationToken ct)
        {
            var publicBase = (await _settings.GetRawAsync("Event.PublicBaseUrl", ct))?.TrimEnd('/')
                             ?? "https://www.axislb.com";
            var eventKey = string.IsNullOrWhiteSpace(ev) ? "squid-game-x-axis" : ev;

            if (!string.IsNullOrWhiteSpace(externalId))
            {
                var verified = await _whish.VerifyAsync(externalId, ct);
                if (verified)
                {
                    await _svc.MarkPaidByProviderRefAsync(externalId, "whish-verified", ct);
                    return Redirect($"{publicBase}/events/{eventKey}/paid?reg={reg}");
                }
            }

            return Redirect($"{publicBase}/events/{eventKey}?payment=failed&reg={reg}");
        }

        /// <summary>
        /// Stripe webhook. Verifies the signature with the stored webhook
        /// secret, then marks the matching registration paid.
        /// </summary>
        [HttpPost("stripe/webhook")]
        public async Task<IActionResult> StripeWebhook(CancellationToken ct)
        {
            string raw;
            using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
                raw = await reader.ReadToEndAsync(ct);

            var secret = await _settings.GetRawAsync("Stripe.WebhookSecret", ct);
            var sigHeader = Request.Headers["Stripe-Signature"].ToString();

            if (!string.IsNullOrWhiteSpace(secret) && !VerifyStripeSignature(raw, sigHeader, secret))
            {
                _logger.LogWarning("Stripe webhook signature verification failed.");
                return Unauthorized();
            }

            try
            {
                using var jd = JsonDocument.Parse(raw);
                var type = jd.RootElement.GetProperty("type").GetString();

                // Only the completed-checkout event matters for us.
                if (type == "checkout.session.completed")
                {
                    var obj = jd.RootElement.GetProperty("data").GetProperty("object");
                    var sessionId = obj.GetProperty("id").GetString();
                    if (!string.IsNullOrWhiteSpace(sessionId))
                        await _svc.MarkPaidByProviderRefAsync(sessionId, raw, ct);
                }

                // Always 200 — a non-2xx makes Stripe retry forever.
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stripe webhook processing failed.");
                return Ok();
            }
        }

        /// <summary>
        /// Stripe signs the raw body as HMAC-SHA256 over "{timestamp}.{payload}".
        /// Header looks like: t=1699999999,v1=abc123...
        /// </summary>
        private static bool VerifyStripeSignature(string payload, string header, string secret)
        {
            if (string.IsNullOrWhiteSpace(header)) return false;

            string? timestamp = null;
            var signatures = new List<string>();
            foreach (var part in header.Split(','))
            {
                var kv = part.Split('=', 2);
                if (kv.Length != 2) continue;
                if (kv[0] == "t") timestamp = kv[1];
                else if (kv[0] == "v1") signatures.Add(kv[1]);
            }
            if (timestamp is null || signatures.Count == 0) return false;

            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var computed = Convert.ToHexString(
                hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();

            // Fixed-time comparison against every provided signature.
            return signatures.Any(s =>
                CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(s.ToLowerInvariant()),
                    Encoding.UTF8.GetBytes(computed)));
        }
    }

    /// <summary>Admin panel — list, stats and payment confirmation.</summary>
    [ApiController]
    [Route("api/admin/event-registrations")]
    [Authorize(Roles = "admin")]
    public class EventRegistrationsAdminController : ControllerBase
    {
        private readonly IEventRegistrationService _svc;
        private readonly IHttpContextAccessor _http;

        public EventRegistrationsAdminController(IEventRegistrationService svc, IHttpContextAccessor http)
        {
            _svc = svc;
            _http = http;
        }

        private string Actor => _http.HttpContext?.User?.Identity?.Name ?? "admin";

        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? eventKey,
            [FromQuery] string? paymentStatus,
            [FromQuery] string? paymentMethod,
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
            => Ok(await _svc.ListAsync(
                new EventRegistrationFilterDto(eventKey, paymentStatus, paymentMethod, search, page, pageSize), ct));

        [HttpGet("stats")]
        public async Task<IActionResult> Stats([FromQuery] string? eventKey, CancellationToken ct)
            => Ok(await _svc.GetStatsAsync(eventKey, ct));

        [HttpPost("{id:int}/confirm")]
        public async Task<IActionResult> Confirm(int id, [FromBody] ConfirmPaymentRequestDto? body, CancellationToken ct)
            => await _svc.ConfirmPaymentAsync(id, Actor, body?.Notes, ct) ? NoContent() : NotFound();

        [HttpPost("{id:int}/reject")]
        public async Task<IActionResult> Reject(int id, [FromBody] ConfirmPaymentRequestDto? body, CancellationToken ct)
            => await _svc.RejectPaymentAsync(id, Actor, body?.Notes, ct) ? NoContent() : NotFound();

        /// <summary>
        /// One-off repair: books journal entries for registrations that were
        /// already marked Paid before ledger posting existed. Idempotent.
        /// Call with ?dryRun=true first to see what it would do.
        /// </summary>
        [HttpPost("backfill-ledger")]
        public async Task<IActionResult> BackfillLedger(
            [FromQuery] string? eventKey,
            [FromQuery] bool dryRun = true,
            CancellationToken ct = default)
            => Ok(await _svc.BackfillLedgerAsync(eventKey, dryRun, ct));
    }
}
