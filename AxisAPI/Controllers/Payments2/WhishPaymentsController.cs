using Application.DTOs;
using Application.IServices;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Payments2
{
    /// <summary>
    /// Whish Collect against an existing open invoice. Staff start from the
    /// till; guests (self-order / kiosk) start with the token that owns the
    /// invoice; everyone polls the status; Whish bounces the buyer to /return.
    /// </summary>
    [ApiController]
    [Route("api/payments/whish")]
    public class WhishPaymentsController : ControllerBase
    {
        private readonly IWhishInvoicePaymentService _svc;
        private readonly ILogger<WhishPaymentsController> _logger;
        public WhishPaymentsController(IWhishInvoicePaymentService svc, ILogger<WhishPaymentsController> logger) { _svc = svc; _logger = logger; }

        /// <summary>Till: create a collect request for the invoice's remaining amount (source Till | Display).</summary>
        [HttpPost("start")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> Start([FromBody] WhishStartDto dto, CancellationToken ct)
        {
            if (dto is null || dto.TransactionId <= 0) return BadRequest(new BaseResponse<WhishStartResultDto>(false, "Validation", "transactionId is required."));
            var (ok, ko) = WhishReturnUrls.Build(Request);
            var r = await _svc.StartAsync(dto.TransactionId, dto.Source ?? "Till", ok, ko, SafeReturnUrl(dto.ReturnUrl), ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        /// <summary>Guest: the self-order session token or kiosk token must own the invoice.</summary>
        [HttpPost("start-public")]
        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 20, WindowSeconds = 60)]
        public async Task<IActionResult> StartPublic([FromBody] WhishStartPublicDto dto, CancellationToken ct)
        {
            if (dto is null || dto.TransactionId <= 0 || string.IsNullOrWhiteSpace(dto.Token))
                return BadRequest(new BaseResponse<WhishStartResultDto>(false, "Validation", "token and transactionId are required."));
            if (!await _svc.ValidatePublicTokenAsync(dto.Token, dto.TransactionId, ct))
                return Unauthorized(new BaseResponse<WhishStartResultDto>(false, "Unauthorized", "This token does not own that invoice."));

            var source = Guid.TryParse(dto.Token.Trim(), out _) ? "SelfOrder" : "Kiosk";
            var (ok, ko) = WhishReturnUrls.Build(Request);
            var r = await _svc.StartAsync(dto.TransactionId, source, ok, ko, SafeReturnUrl(dto.ReturnUrl), ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        /// <summary>Status only (no business data) — polled by phones, kiosks and the till.</summary>
        [HttpGet("{requestId:int}/status")]
        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 120, WindowSeconds = 60)]
        public async Task<IActionResult> Status(int requestId, CancellationToken ct)
        {
            var r = await _svc.CheckAsync(requestId, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        /// <summary>
        /// Whish redirects the buyer here. The redirect is never trusted: the
        /// service re-asks Whish, then the buyer is bounced to the UI URL given
        /// at start (or a bare page when none was given).
        /// </summary>
        [HttpGet("return")]
        [AllowAnonymous]
        public async Task<IActionResult> Return([FromQuery] int? requestId, [FromQuery] string? result, CancellationToken ct)
        {
            if (requestId is null || requestId <= 0) return Content("Missing requestId.", "text/plain");
            var r = await _svc.CheckAsync(requestId.Value, ct);
            var status = r.Data?.Status ?? "Unknown";
            _logger.LogInformation("Whish return for request {Req}: provider said {Result}, verified {Status}", requestId, result, status);

            var target = r.Data is null ? null : await _svc.GetReturnUrlAsync(requestId.Value, ct);
            if (!string.IsNullOrWhiteSpace(target))
            {
                var sep = target.Contains('?') ? "&" : "?";
                return Redirect($"{target}{sep}whish={Uri.EscapeDataString(status.ToLowerInvariant())}&requestId={requestId}");
            }
            var paid = status == "Paid";
            return Content($"<html><body style=\"font-family:sans-serif;text-align:center;padding:40px\"><h1>{(paid ? "Payment received — thank you!" : "Payment not completed")}</h1><p>Request #{requestId}: {status}. You can close this window.</p></body></html>", "text/html");
        }

        /// <summary>Only relative paths or http(s) URLs are accepted as UI return targets.</summary>
        private static string? SafeReturnUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            var u = url.Trim();
            if (u.StartsWith("/") && !u.StartsWith("//")) return u;
            return Uri.TryCreate(u, UriKind.Absolute, out var abs) && (abs.Scheme == "http" || abs.Scheme == "https") ? u : null;
        }
    }
}
