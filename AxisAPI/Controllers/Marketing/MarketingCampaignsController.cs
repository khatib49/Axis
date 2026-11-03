using Application.DTOs;
using Application.IServices;
using Application.Services.Marketing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Marketing
{
    [ApiController]
    [Route("api/marketing/campaigns")]
    [Authorize(Roles = "admin")]
    public class MarketingCampaignsController : ControllerBase
    {
        private readonly IMarketingCampaignService _svc;
        public MarketingCampaignsController(IMarketingCampaignService svc) => _svc = svc;

        [HttpGet("channels")]
        public IActionResult Channels() => Ok(MarketingCampaignService.Channels);

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct) => Ok(await _svc.ListAsync(status, ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var c = await _svc.GetAsync(id, ct);
            return c is null ? NotFound() : Ok(c);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CampaignCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? CreatedAtAction(nameof(Get), new { id = r.Data!.Id }, r.Data) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] CampaignUpdateDto dto, CancellationToken ct) => Map(await _svc.UpdateAsync(id, dto, ct));

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteAsync(id, ct);
            if (r.Success) return NoContent();
            return r.Message == "Not found" ? NotFound(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/schedule")]
        public async Task<IActionResult> Schedule(int id, [FromBody] CampaignScheduleDto dto, CancellationToken ct) => Map(await _svc.ScheduleAsync(id, dto.ScheduledFor, ct));

        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, CancellationToken ct) => Map(await _svc.CancelAsync(id, ct));

        /// <summary>Builds recipients from the segment and sends immediately. Returns the send summary.</summary>
        [HttpPost("{id:int}/send-now")]
        public async Task<IActionResult> SendNow(int id, CancellationToken ct) => Map(await _svc.SendNowAsync(id, ct));

        /// <summary>Sends one rendered message to the given address (recorded as a test recipient).</summary>
        [HttpPost("{id:int}/test-send")]
        public async Task<IActionResult> TestSend(int id, [FromBody] CampaignTestSendDto dto, CancellationToken ct) => Map(await _svc.TestSendAsync(id, dto.Address, ct));

        [HttpGet("{id:int}/stats")]
        public async Task<IActionResult> Stats(int id, CancellationToken ct) => Map(await _svc.StatsAsync(id, ct));

        [HttpGet("{id:int}/recipients")]
        public async Task<IActionResult> Recipients(int id, [FromQuery] string? status, [FromQuery] string? search,
            [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
            Ok(await _svc.RecipientsAsync(id, status, search, page, pageSize, ct));

        private IActionResult Map<T>(BaseResponse<T> r)
        {
            if (r.Success) return Ok(r.Data);
            return r.Message == "Not found" ? NotFound(r) : BadRequest(r);
        }
    }
}
