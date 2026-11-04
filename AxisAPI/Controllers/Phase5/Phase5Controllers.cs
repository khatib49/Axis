using System.Text;
using Application.DTOs;
using Application.IServices;
using Application.Services.Phase5;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Phase5
{
    internal static class R
    {
        public static IActionResult From<T>(ControllerBase c, BaseResponse<T> r) => r.Success ? c.Ok(r)
            : r.Error switch { "NotFound" => c.NotFound(r), "Unauthorized" => c.Unauthorized(r), _ => c.BadRequest(r) };
    }

    /// <summary>Tournaments: staff run the bracket; admins create and pay prizes; guests see the bracket and sign up.</summary>
    [ApiController]
    [Route("api/tournaments")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class TournamentsController : ControllerBase
    {
        private readonly ITournamentService _svc;
        public TournamentsController(ITournamentService svc) { _svc = svc; }

        [HttpGet] public async Task<IActionResult> List([FromQuery] bool upcomingOnly = false, CancellationToken ct = default) => Ok(await _svc.ListAsync(upcomingOnly, ct));
        [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct) => await _svc.GetAsync(id, ct) is { } t ? Ok(t) : NotFound();
        [HttpPost][Authorize(Roles = "admin")] public async Task<IActionResult> Create([FromBody] TournamentUpsertDto dto, CancellationToken ct) => R.From(this, await _svc.CreateAsync(dto, ct));
        [HttpPut("{id:int}")][Authorize(Roles = "admin")] public async Task<IActionResult> Update(int id, [FromBody] TournamentUpsertDto dto, CancellationToken ct) => R.From(this, await _svc.UpdateAsync(id, dto, ct));
        [HttpPost("{id:int}/status")][Authorize(Roles = "admin")] public async Task<IActionResult> Status(int id, [FromQuery] string status, CancellationToken ct) => R.From(this, await _svc.SetStatusAsync(id, status, ct));
        [HttpPost("{id:int}/players")] public async Task<IActionResult> AddPlayer(int id, [FromBody] TournamentAddPlayerDto dto, CancellationToken ct) => R.From(this, await _svc.AddPlayerAsync(id, dto, ct));
        [HttpPut("{id:int}/players/{pid:int}")] public async Task<IActionResult> UpdatePlayer(int id, int pid, [FromBody] TournamentAddPlayerDto dto, CancellationToken ct) => R.From(this, await _svc.UpdatePlayerAsync(id, pid, dto, ct));
        [HttpDelete("{id:int}/players/{pid:int}")] public async Task<IActionResult> RemovePlayer(int id, int pid, CancellationToken ct) => R.From(this, await _svc.RemovePlayerAsync(id, pid, ct));
        [HttpPost("{id:int}/start")][Authorize(Roles = "admin")] public async Task<IActionResult> Start(int id, CancellationToken ct) => R.From(this, await _svc.StartAsync(id, ct));
        [HttpPost("{id:int}/matches/{mid:int}/result")] public async Task<IActionResult> Result(int id, int mid, [FromBody] TournamentResultDto dto, CancellationToken ct) => R.From(this, await _svc.ReportResultAsync(id, mid, dto, ct));
        [HttpPost("{id:int}/pay-prizes")][Authorize(Roles = "admin")] public async Task<IActionResult> PayPrizes(int id, CancellationToken ct) => R.From(this, await _svc.PayPrizesAsync(id, ct));

        [HttpGet("public/{token}")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 120, WindowSeconds = 60)]
        public async Task<IActionResult> Public(string token, CancellationToken ct) => await _svc.GetPublicAsync(token, ct) is { } t ? Ok(t) : NotFound();
        [HttpGet("public")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 60, WindowSeconds = 60)]
        public async Task<IActionResult> PublicList(CancellationToken ct) => Ok(await _svc.ListAsync(true, ct));
        [HttpPost("public/{token}/register")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 10, WindowSeconds = 60)]
        public async Task<IActionResult> Register(string token, [FromBody] TournamentRegisterDto dto, CancellationToken ct) => R.From(this, await _svc.RegisterPublicAsync(token, dto, ct));
    }

    /// <summary>Station booking: public availability and booking; staff see and update bookings.</summary>
    [ApiController]
    [Route("api/bookings")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class StationBookingsController : ControllerBase
    {
        private readonly IBookingService _svc;
        public StationBookingsController(IBookingService svc) { _svc = svc; }

        [HttpGet] public async Task<IActionResult> List([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        {
            var f = from ?? DateTime.UtcNow.Date.AddDays(-1); var t = to ?? f.AddDays(15);
            return Ok(await _svc.ListAsync(f, t, ct));
        }
        [HttpPost] public async Task<IActionResult> Create([FromBody] BookingCreateDto dto, CancellationToken ct) => R.From(this, await _svc.CreateAsync(dto, true, ct));
        [HttpPost("{id:int}/status")] public async Task<IActionResult> Status(int id, [FromQuery] string status, CancellationToken ct) => R.From(this, await _svc.SetStatusAsync(id, status, ct));

        [HttpGet("public/settings")][AllowAnonymous] public async Task<IActionResult> Settings(CancellationToken ct) => Ok(await _svc.SettingsAsync(ct));
        [HttpGet("public/stations")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 60, WindowSeconds = 60)] public async Task<IActionResult> Stations(CancellationToken ct) => Ok(await _svc.StationsAsync(ct));
        [HttpGet("public/availability")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 120, WindowSeconds = 60)]
        public async Task<IActionResult> Availability([FromQuery] int setId, [FromQuery] DateTime date, [FromQuery] int duration = 60, CancellationToken ct = default) => R.From(this, await _svc.AvailabilityAsync(setId, date, duration, ct));
        [HttpPost("public")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 6, WindowSeconds = 60)]
        public async Task<IActionResult> Book([FromBody] BookingCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, false, ct);
            if (!r.Success || r.Data is null) return R.From(this, r);
            return Ok(new BaseResponse<BookingPublicDto>(true, null, r.Message, await _svc.GetPublicAsync(r.Data.Token, ct)));
        }
        [HttpGet("public/{token}")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 60, WindowSeconds = 60)]
        public async Task<IActionResult> Get(string token, CancellationToken ct) => await _svc.GetPublicAsync(token, ct) is { } b ? Ok(b) : NotFound();
        [HttpPost("public/{token}/cancel")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 10, WindowSeconds = 60)]
        public async Task<IActionResult> Cancel(string token, CancellationToken ct) => R.From(this, await _svc.CancelPublicAsync(token, ct));
        [HttpPost("public/{token}/check-in")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 10, WindowSeconds = 60)]
        public async Task<IActionResult> CheckIn(string token, CancellationToken ct) => R.From(this, await _svc.CheckInPublicAsync(token, ct));
    }

    /// <summary>Ordering from the station QR (public) and the station QR codes (admin).</summary>
    [ApiController]
    [Route("api/station")]
    [Authorize(Roles = "admin")]
    public class StationOrdersController : ControllerBase
    {
        private readonly IStationOrderService _svc;
        public StationOrdersController(IStationOrderService svc) { _svc = svc; }

        [HttpGet("codes")] public async Task<IActionResult> Codes(CancellationToken ct) => Ok(await _svc.ListCodesAsync(ct));
        [HttpPost("codes/{setId:int}")] public async Task<IActionResult> Ensure(int setId, [FromQuery] bool rotate = false, CancellationToken ct = default)
        {
            try { return Ok(await _svc.EnsureCodeAsync(setId, rotate, ct)); } catch (KeyNotFoundException e) { return NotFound(new { message = e.Message }); }
        }

        [HttpGet("public/{token}")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 60, WindowSeconds = 60)]
        public async Task<IActionResult> Get(string token, CancellationToken ct) => await _svc.GetAsync(token, ct) is { } s ? Ok(s) : NotFound();
        [HttpPost("public/{token}/order")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 10, WindowSeconds = 60)]
        public async Task<IActionResult> Order(string token, [FromBody] StationOrderDto dto, CancellationToken ct) => R.From(this, await _svc.OrderAsync(token, dto, ct));
        [HttpPost("public/{token}/extend")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 5, WindowSeconds = 60)]
        public async Task<IActionResult> Extend(string token, [FromQuery] int minutes, CancellationToken ct) => R.From(this, await _svc.RequestExtensionAsync(token, minutes, ct));
    }

    /// <summary>Owner assistant: Telegram and WhatsApp webhooks (anonymous, verified) plus admin setup.</summary>
    [ApiController]
    [Route("api/owner-bot")]
    [Authorize(Roles = "admin")]
    public class OwnerBotController : ControllerBase
    {
        private readonly IOwnerBotService _svc;
        private readonly ILogger<OwnerBotController> _log;
        public OwnerBotController(IOwnerBotService svc, ILogger<OwnerBotController> log) { _svc = svc; _log = log; }

        [HttpGet("status")] public async Task<IActionResult> Status(CancellationToken ct) => Ok(await _svc.StatusAsync(ct));
        [HttpPost("chats/{id:int}/allow")] public async Task<IActionResult> Allow(int id, [FromQuery] bool value, CancellationToken ct) => R.From(this, await _svc.SetAllowedAsync(id, value, ct));
        [HttpGet("summary")] public async Task<IActionResult> Summary(CancellationToken ct) => Ok(new { text = await _svc.SummaryTextAsync(ct) });
        [HttpPost("summary/send")] public async Task<IActionResult> SendSummary(CancellationToken ct) => Ok(await _svc.SendDailySummaryAsync(true, ct));
        [HttpPost("ask")] public async Task<IActionResult> Ask([FromBody] AskDto dto, CancellationToken ct) => Ok(new { text = await _svc.AnswerAsync("Telegram", "dashboard-test", "Dashboard", dto.Text, ct) });
        [HttpPost("telegram/set-webhook")] public async Task<IActionResult> SetWebhook([FromQuery] string? publicBaseUrl, CancellationToken ct)
            => R.From(this, await _svc.SetTelegramWebhookAsync(string.IsNullOrWhiteSpace(publicBaseUrl) ? $"{Request.Scheme}://{Request.Host}" : publicBaseUrl, ct));

        public record AskDto(string Text);

        [HttpPost("telegram")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 60, WindowSeconds = 60)]
        public async Task<IActionResult> Telegram(CancellationToken ct)
        {
            if (!await _svc.CheckTelegramSecretAsync(Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault(), ct)) return Unauthorized();
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var raw = await reader.ReadToEndAsync(ct);
            try { await _svc.HandleTelegramAsync(raw, ct); } catch (Exception ex) { _log.LogWarning(ex, "Telegram update failed"); }
            return Ok(); // Telegram retries non-200 answers forever.
        }

        [HttpGet("whatsapp")][AllowAnonymous]
        public async Task<IActionResult> WhatsAppVerify([FromQuery(Name = "hub.mode")] string? mode, [FromQuery(Name = "hub.verify_token")] string? token, [FromQuery(Name = "hub.challenge")] string? challenge, CancellationToken ct)
            => await _svc.VerifyWhatsAppAsync(mode, token, challenge, ct) is { } c ? Content(c, "text/plain") : Unauthorized();

        [HttpPost("whatsapp")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 120, WindowSeconds = 60)]
        public async Task<IActionResult> WhatsApp(CancellationToken ct)
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            var raw = await reader.ReadToEndAsync(ct);
            if (!await _svc.CheckWhatsAppSignatureAsync(Request.Headers["X-Hub-Signature-256"].FirstOrDefault(), raw, ct)) return Unauthorized();
            try { await _svc.HandleWhatsAppAsync(raw, ct); } catch (Exception ex) { _log.LogWarning(ex, "WhatsApp update failed"); }
            return Ok();
        }
    }

    /// <summary>Parent wallet controls (staff set them up at the counter) and the parent's read-only page.</summary>
    [ApiController]
    [Route("api/parent-wallet")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class ParentWalletController : ControllerBase
    {
        private readonly IParentWalletService _svc;
        public ParentWalletController(IParentWalletService svc) { _svc = svc; }
        [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));
        [HttpPost] public async Task<IActionResult> Upsert([FromBody] ParentLinkUpsertDto dto, CancellationToken ct) => R.From(this, await _svc.UpsertAsync(dto, ct));
        [HttpDelete("{id:int}")][Authorize(Roles = "admin")] public async Task<IActionResult> Delete(int id, CancellationToken ct) => await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();
        [HttpPost("notify-now")][Authorize(Roles = "admin")] public async Task<IActionResult> NotifyNow(CancellationToken ct) => Ok(new { sent = await _svc.NotifySessionEndsAsync(ct) });
        [HttpGet("public/{token}")][AllowAnonymous][AnonymousRateLimit(MaxRequests = 60, WindowSeconds = 60)]
        public async Task<IActionResult> Public(string token, CancellationToken ct) => await _svc.PublicViewAsync(token, ct) is { } v ? Ok(v) : NotFound();
    }

    [ApiController]
    [Route("api/alerts")]
    [Authorize(Roles = "admin")]
    public class OwnerAlertsController : ControllerBase
    {
        private readonly IAlertService _svc;
        public OwnerAlertsController(IAlertService svc) { _svc = svc; }
        [HttpGet("metrics")] public IActionResult Metrics() => Ok(Domain.Entities.AlertMetrics.All.Select(m => new { metric = m, label = AlertService.Label(m) }));
        [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));
        [HttpPost] public async Task<IActionResult> Create([FromBody] AlertRuleUpsertDto dto, CancellationToken ct) => R.From(this, await _svc.CreateAsync(dto, ct));
        [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, [FromBody] AlertRuleUpsertDto dto, CancellationToken ct) => R.From(this, await _svc.UpdateAsync(id, dto, ct));
        [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) => await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();
        [HttpPost("{id:int}/test")] public async Task<IActionResult> Test(int id, [FromQuery] bool send = false, CancellationToken ct = default) => R.From(this, await _svc.TestAsync(id, send, ct));
        [HttpPost("run")] public async Task<IActionResult> Run(CancellationToken ct) => Ok(await _svc.RunAsync(ct));
        [HttpGet("events")] public async Task<IActionResult> Events([FromQuery] int take = 100, CancellationToken ct = default) => Ok(await _svc.EventsAsync(take, ct));
    }

    [ApiController]
    [Route("api/ai-tools")]
    [Authorize(Roles = "admin")]
    public class AiToolsController : ControllerBase
    {
        private readonly IAiToolsService _svc;
        public AiToolsController(IAiToolsService svc) { _svc = svc; }
        [HttpGet("forecast")] public async Task<IActionResult> Forecast([FromQuery] int days = 7, CancellationToken ct = default) => Ok(await _svc.ForecastAsync(days, ct));
        [HttpPost("extract/{kind}")][RequestSizeLimit(12_000_000)]
        public async Task<IActionResult> Extract(string kind, IFormFile file, CancellationToken ct) => R.From(this, await _svc.ExtractAsync(kind, file, ct));
        [HttpGet("drafts")] public async Task<IActionResult> Drafts([FromQuery] string? kind, CancellationToken ct) => Ok(await _svc.ListDraftsAsync(kind, ct));
        public record DraftJsonDto(string Json);
        [HttpPut("drafts/{id:int}")] public async Task<IActionResult> UpdateDraft(int id, [FromBody] DraftJsonDto dto, CancellationToken ct) => R.From(this, await _svc.UpdateDraftAsync(id, dto.Json, ct));
        [HttpDelete("drafts/{id:int}")] public async Task<IActionResult> Discard(int id, CancellationToken ct) => await _svc.DiscardAsync(id, ct) ? NoContent() : NotFound();
        [HttpPost("drafts/{id:int}/apply-menu")] public async Task<IActionResult> ApplyMenu(int id, [FromBody] MenuApplyDto dto, CancellationToken ct) => R.From(this, await _svc.ApplyMenuAsync(id, dto ?? new(), ct));
        [HttpPost("drafts/{id:int}/apply-invoice")] public async Task<IActionResult> ApplyInvoice(int id, [FromBody] InvoiceApplyDto dto, CancellationToken ct) => R.From(this, await _svc.ApplyInvoiceAsync(id, dto, ct));
    }

    [ApiController]
    [Route("api/setup")]
    [Authorize(Roles = "admin")]
    public class SetupController : ControllerBase
    {
        private readonly ISetupService _svc;
        public SetupController(ISetupService svc) { _svc = svc; }
        [HttpGet] public async Task<IActionResult> Status(CancellationToken ct) => Ok(await _svc.StatusAsync(ct));
        [HttpPost("business")] public async Task<IActionResult> Business([FromBody] SetupBusinessDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.SaveBusinessAsync(dto, User?.Identity?.Name ?? "admin", ct)); } catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        }
    }

    [ApiController]
    [Route("api/accounting/exports")]
    [Authorize(Roles = "admin")]
    public class AccountingExportsController : ControllerBase
    {
        private readonly IAccountingExportService _svc;
        public AccountingExportsController(IAccountingExportService svc) { _svc = svc; }
        [HttpGet("journals/{format}")]
        public async Task<IActionResult> Journals(string format, [FromQuery] DateTime from, [FromQuery] DateTime to, CancellationToken ct)
        {
            try
            {
                var (name, csv, _) = await _svc.ExportAsync(format, DateTime.SpecifyKind(from, DateTimeKind.Utc), DateTime.SpecifyKind(to, DateTimeKind.Utc), ct);
                return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", name);
            }
            catch (ArgumentException e) { return BadRequest(new { message = e.Message }); }
        }
        [HttpGet("accounts")]
        public async Task<IActionResult> Accounts(CancellationToken ct)
        {
            var (name, csv) = await _svc.ChartOfAccountsAsync(ct);
            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv", name);
        }
    }
}
