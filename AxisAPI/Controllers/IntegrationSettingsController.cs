using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
    }
}
