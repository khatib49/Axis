using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Marketing
{
    /// <summary>
    /// GET api/marketing/unsubscribe?token= is the public link embedded in e-mails;
    /// the rest is the admin view of the suppression list.
    /// </summary>
    [ApiController]
    [Route("api/marketing/unsubscribe")]
    [Authorize(Roles = "admin")]
    public class MarketingUnsubscribesController : ControllerBase
    {
        private readonly IMarketingUnsubscribeService _svc;
        public MarketingUnsubscribesController(IMarketingUnsubscribeService svc) => _svc = svc;

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Unsubscribe([FromQuery] string? token, CancellationToken ct)
        {
            var ok = await _svc.UnsubscribeByTokenAsync(token, ct);
            var html = ok
                ? "<!doctype html><html><body style=\"font-family:sans-serif;text-align:center;padding:40px\"><h2>You have been unsubscribed</h2><p>You will no longer receive marketing messages from us.</p></body></html>"
                : "<!doctype html><html><body style=\"font-family:sans-serif;text-align:center;padding:40px\"><h2>Invalid unsubscribe link</h2><p>The link is incomplete or has expired.</p></body></html>";
            return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = ok ? 200 : 400 };
        }

        [HttpGet("list")]
        public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));

        [HttpPost("list")]
        public async Task<IActionResult> Add([FromBody] UnsubscribeCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.AddAsync(dto.Address, "Manual", ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpDelete("list/{id:int}")]
        public async Task<IActionResult> Remove(int id, CancellationToken ct) => await _svc.RemoveAsync(id, ct) ? NoContent() : NotFound();

        /// <summary>Admin helper: the signed link for an address (to test the flow).</summary>
        [HttpGet("link")]
        public IActionResult Link([FromQuery] string address) =>
            string.IsNullOrWhiteSpace(address) ? BadRequest("address required") : Ok(new { address, url = _svc.BuildUnsubscribeUrl(address), token = _svc.CreateToken(address) });
    }
}
