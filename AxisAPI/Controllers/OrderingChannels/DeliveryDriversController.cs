using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.OrderingChannels
{
    /// <summary>Delivery drivers. Admin manages; till reads the list to assign.</summary>
    [ApiController]
    [Route("api/delivery/drivers")]
    [Authorize(Roles = "admin")]
    public class DeliveryDriversController : ControllerBase
    {
        private readonly IDeliveryService _svc;
        public DeliveryDriversController(IDeliveryService svc) { _svc = svc; }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
            => Ok(await _svc.ListDriversAsync(includeInactive, ct));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DriverUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateDriverAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] DriverUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateDriverAsync(id, dto, ct);
            if (!r.Success && r.Error == "NotFound") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteDriverAsync(id, ct) ? NoContent() : NotFound();
    }
}
