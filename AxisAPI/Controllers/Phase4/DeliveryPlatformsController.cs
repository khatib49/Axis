using Application.DTOs;
using Application.IServices;
using AxisAPI.Controllers.OrderingChannels;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Phase4
{
    /// <summary>
    /// Connected delivery apps: setup, item mapping, status push log (admin)
    /// and the menu feed the app pulls (anonymous, X-Webhook-Secret).
    /// </summary>
    [ApiController]
    [Route("api/delivery/platforms")]
    [Authorize(Roles = "admin")]
    public class DeliveryPlatformsController : ControllerBase
    {
        private readonly IDeliveryPlatformService _svc;
        public DeliveryPlatformsController(IDeliveryPlatformService svc) { _svc = svc; }

        private string AssetBase => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DeliveryPlatformUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DeliveryPlatformUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            return r.Success ? Ok(r) : r.Error == "NotFound" ? NotFound(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct) => await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();

        [HttpGet("{id:int}/mappings")]
        public async Task<IActionResult> Mappings(int id, CancellationToken ct) => Ok(await _svc.ListMappingsAsync(id, ct));

        [HttpPut("{id:int}/mappings")]
        public async Task<IActionResult> UpsertMapping(int id, [FromBody] DeliveryItemMappingUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpsertMappingAsync(id, dto, ct);
            return r.Success ? Ok(r) : r.Error == "NotFound" ? NotFound(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}/mappings/{mappingId:int}")]
        public async Task<IActionResult> DeleteMapping(int id, int mappingId, CancellationToken ct)
            => await _svc.DeleteMappingAsync(id, mappingId, ct) ? NoContent() : NotFound();

        [HttpGet("{id:int}/menu-preview")]
        public async Task<IActionResult> MenuPreview(int id, CancellationToken ct)
        {
            var r = await _svc.MenuPreviewAsync(id, AssetBase, ct);
            return r.Success ? Ok(r.Data) : NotFound(r);
        }

        [HttpGet("{id:int}/pushes")]
        public async Task<IActionResult> Pushes(int id, [FromQuery] int take = 50, CancellationToken ct = default)
            => Ok(await _svc.ListPushesAsync(id, take, ct));

        [HttpPost("{id:int}/pushes/{pushId:int}/retry")]
        public async Task<IActionResult> Retry(int id, int pushId, CancellationToken ct)
            => await _svc.RetryPushAsync(id, pushId, ct) ? Ok(new { retried = true }) : NotFound();

        [HttpPost("{id:int}/test-callback")]
        public async Task<IActionResult> TestCallback(int id, CancellationToken ct) => Ok(await _svc.TestCallbackAsync(id, ct));

        /// <summary>Send due status updates now (the job also runs every minute).</summary>
        [HttpPost("run-pushes")]
        public async Task<IActionResult> RunPushes(CancellationToken ct) => Ok(await _svc.RunPushesAsync(ct));

        /// <summary>The app pulls the menu here with header X-Webhook-Secret.</summary>
        [HttpGet("{source}/menu")]
        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 30, WindowSeconds = 60)]
        public async Task<IActionResult> MenuFeed(string source, CancellationToken ct)
        {
            var r = await _svc.MenuFeedAsync(source, Request.Headers[DeliveryWebhookController.SecretHeader].FirstOrDefault(), AssetBase, ct);
            if (r.Success) return Ok(r.Data);
            return r.Error switch { "Unauthorized" => Unauthorized(r), "NotFound" or "NotConfigured" => NotFound(r), _ => BadRequest(r) };
        }
    }
}
