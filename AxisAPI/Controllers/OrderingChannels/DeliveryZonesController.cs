using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.OrderingChannels
{
    /// <summary>Delivery zones (fee, minimum order, ETA). Admin manages; till reads.</summary>
    [ApiController]
    [Route("api/delivery/zones")]
    [Authorize(Roles = "admin")]
    public class DeliveryZonesController : ControllerBase
    {
        private readonly IDeliveryService _svc;
        public DeliveryZonesController(IDeliveryService svc) { _svc = svc; }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
            => Ok(await _svc.ListZonesAsync(includeInactive, ct));

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var z = await _svc.GetZoneAsync(id, ct);
            return z is null ? NotFound() : Ok(z);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DeliveryZoneUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateZoneAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DeliveryZoneUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateZoneAsync(id, dto, ct);
            if (!r.Success && r.Error == "NotFound") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteZoneAsync(id, ct) ? NoContent() : NotFound();
    }
}
