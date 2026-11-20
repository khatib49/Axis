using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/integration-settings")]
    [Authorize(Roles = "admin")]
    public class IntegrationSettingsController : ControllerBase
    {
        private readonly IIntegrationSettingsService _svc;
        public IntegrationSettingsController(IIntegrationSettingsService svc) => _svc = svc;

        // GET /api/integration-settings  — secrets are masked
        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
            => Ok(await _svc.ListAsync(ct));

        // PUT /api/integration-settings  — upsert one row
        [HttpPut]
        public async Task<IActionResult> Upsert([FromBody] IntegrationSettingUpdateDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Key))
                return BadRequest("Key is required.");

            var actor = User?.Identity?.Name ?? "system";
            await _svc.UpsertAsync(dto.Key, dto.Value, actor, ct);
            return NoContent();
        }

        [HttpPost("test/anthropic")]
        public async Task<IActionResult> TestAnthropic(CancellationToken ct)
            => Ok(await _svc.TestAnthropicAsync(ct));

        [HttpPost("test/whatsapp")]
        public async Task<IActionResult> TestWhatsApp(CancellationToken ct)
            => Ok(await _svc.TestWhatsAppAsync(ct));

        /// <summary>
        /// Sends a test email through the saved Smtp.* settings to the signed-in
        /// admin's own address (never an address from the request).
        /// </summary>
        [HttpPost("test/email")]
        public async Task<IActionResult> TestEmail([FromServices] IEmailSender email, CancellationToken ct)
        {
            var to = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
            if (string.IsNullOrWhiteSpace(to))
                return Ok(new IntegrationTestResultDto(false, "Your account has no email address to send the test to."));

            var res = await email.SendAsync(new[] { to }, "Axis test email",
                "<p>This is a test from Axis → Integrations → Email (SMTP).</p><p>If you can read this, password reset emails will be delivered.</p>",
                isHtml: true, ct: ct);

            if (res.Sent) return Ok(new IntegrationTestResultDto(true, $"Sent to {to}. Check the inbox (and spam)."));
            return Ok(new IntegrationTestResultDto(false, res.Configured
                ? $"The mail server refused: {res.Error}"
                : "Email isn't set up: fill in Smtp.Host and Smtp.From (and Smtp.User / Smtp.Password)."));
        }
    }
}
