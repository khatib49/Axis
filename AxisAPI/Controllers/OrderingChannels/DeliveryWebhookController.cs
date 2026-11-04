using Application.DTOs;
using Application.IServices;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.OrderingChannels
{
    /// <summary>
    /// Inbound orders from aggregators (Toters etc.). Anonymous, protected by
    /// header X-Webhook-Secret matched against IntegrationSettings key
    /// "Delivery.WebhookSecret.{source}". Idempotent on (source, externalRef).
    /// </summary>
    [ApiController]
    [Route("api/delivery/webhook")]
    [AllowAnonymous]
    [AnonymousRateLimit(MaxRequests = 30, WindowSeconds = 60)]
    public class DeliveryWebhookController : ControllerBase
    {
        public const string SecretHeader = "X-Webhook-Secret";
        private readonly IDeliveryService _svc;
        private readonly IDeliveryPlatformService _platforms;
        public DeliveryWebhookController(IDeliveryService svc, IDeliveryPlatformService platforms) { _svc = svc; _platforms = platforms; }

        [HttpPost("{source}")]
        public async Task<IActionResult> Receive(string source, [FromBody] WebhookOrderPayload payload, CancellationToken ct)
        {
            var secret = Request.Headers[SecretHeader].FirstOrDefault();
            var r = await _svc.IngestWebhookAsync(source, secret, payload, ct);
            if (r.Success && r.Data is { Duplicate: false } d)
                await _platforms.OnOrderIngestedAsync(source, d.Order.Id, ct);
            if (r.Success) return Ok(r);
            return r.Error switch
            {
                "Unauthorized" => Unauthorized(r),
                "NotConfigured" => NotFound(r),
                _ => BadRequest(r),
            };
        }
    }
}
