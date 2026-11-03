using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.OrderingChannels
{
    /// <summary>Admin: kiosk devices and their tokens.</summary>
    [ApiController]
    [Route("api/kiosk/devices")]
    [Authorize(Roles = "admin")]
    public class KioskDevicesController : ControllerBase
    {
        private readonly IKioskService _svc;
        public KioskDevicesController(IKioskService svc) { _svc = svc; }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListDevicesAsync(ct));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] KioskDeviceUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateDeviceAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] KioskDeviceUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateDeviceAsync(id, dto, ct);
            if (!r.Success && r.Error == "NotFound") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPost("{id:int}/rotate-token")]
        public async Task<IActionResult> Rotate(int id, CancellationToken ct)
        {
            var r = await _svc.RotateTokenAsync(id, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteDeviceAsync(id, ct) ? NoContent() : NotFound();
    }
}
