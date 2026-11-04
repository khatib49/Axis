using System.Text;
using System.Text.Json;
using Application.DTOs;
using Application.IServices;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Phase4
{
    /// <summary>
    /// Card terminals. Till roles list terminals and run payments; setup and
    /// the reconciliation report are admin-only; the provider callback is
    /// anonymous and HMAC-signed.
    /// </summary>
    [ApiController]
    [Route("api/payments")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class CardTerminalsController : ControllerBase
    {
        public const string SignatureHeader = "X-Axis-Signature";
        private readonly ICardTerminalService _svc;
        public CardTerminalsController(ICardTerminalService svc) { _svc = svc; }

        [HttpGet("terminals")]
        public async Task<IActionResult> Terminals([FromQuery] bool includeInactive = false, CancellationToken ct = default)
            => Ok(await _svc.ListTerminalsAsync(includeInactive, ct));

        [HttpPost("terminals")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateTerminal([FromBody] CardTerminalUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateTerminalAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("terminals/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateTerminal(int id, [FromBody] CardTerminalUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateTerminalAsync(id, dto, ct);
            return r.Success ? Ok(r) : r.Error == "NotFound" ? NotFound(r) : BadRequest(r);
        }

        [HttpDelete("terminals/{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> DeleteTerminal(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteTerminalAsync(id, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        [HttpPost("terminal/start")]
        public async Task<IActionResult> Start([FromBody] TerminalStartDto dto, CancellationToken ct)
        {
            if (dto is null || dto.TransactionId <= 0 || dto.TerminalId <= 0)
                return BadRequest(new BaseResponse<TerminalPaymentDto>(false, "Validation", "transactionId and terminalId are required."));
            var r = await _svc.StartAsync(dto, ct);
            return r.Success ? Ok(r) : r.Error == "NotFound" ? NotFound(r) : BadRequest(r);
        }

        /// <summary>Poll: also asks the provider for news while the payment is pending.</summary>
        [HttpGet("terminal/{id:int}")]
        public async Task<IActionResult> Check(int id, CancellationToken ct)
        {
            var r = await _svc.CheckAsync(id, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        [HttpPost("terminal/{id:int}/confirm-manual")]
        public async Task<IActionResult> ConfirmManual(int id, [FromBody] TerminalManualConfirmDto dto, CancellationToken ct)
        {
            var r = await _svc.ConfirmManualAsync(id, dto, ct);
            return r.Success ? Ok(r) : r.Error == "NotFound" ? NotFound(r) : BadRequest(r);
        }

        [HttpPost("terminal/{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, CancellationToken ct)
        {
            var r = await _svc.CancelAsync(id, ct);
            return r.Success ? Ok(r) : r.Error == "NotFound" ? NotFound(r) : BadRequest(r);
        }

        [HttpGet("terminal/report")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Report([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? terminalId, CancellationToken ct)
            => Ok(await _svc.ReportAsync(from, to, terminalId, ct));

        /// <summary>Provider pushes the final result here, body signed with CardTerminal.Cloud.WebhookSecret.</summary>
        [HttpPost("terminal/callback")]
        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 60, WindowSeconds = 60)]
        public async Task<IActionResult> Callback(CancellationToken ct)
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var raw = await reader.ReadToEndAsync(ct);
            TerminalCallbackDto? dto;
            try { dto = JsonSerializer.Deserialize<TerminalCallbackDto>(raw, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (JsonException) { dto = null; }
            if (dto is null || string.IsNullOrWhiteSpace(dto.Reference) || string.IsNullOrWhiteSpace(dto.Status))
                return BadRequest(new BaseResponse<TerminalPaymentDto>(false, "Validation", "reference and status are required."));
            var r = await _svc.CallbackAsync(Request.Headers[SignatureHeader].FirstOrDefault(), raw, dto, ct);
            return r.Success ? Ok(r) : r.Error switch { "Unauthorized" => Unauthorized(r), "NotFound" => NotFound(r), _ => BadRequest(r) };
        }
    }
}
