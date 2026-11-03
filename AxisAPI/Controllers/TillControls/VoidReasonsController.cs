using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.TillControls
{
    [ApiController]
    [Route("api/till/void-reasons")]
    public class VoidReasonsController : ControllerBase
    {
        private readonly IVoidRequestService _svc;
        public VoidReasonsController(IVoidRequestService svc) => _svc = svc;

        /// <summary>Active reasons (till roles pick from these); admins may include inactive ones.</summary>
        [HttpGet]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        public async Task<IActionResult> List([FromQuery] bool includeInactive, CancellationToken ct)
            => Ok(await _svc.ListReasonsAsync(includeInactive, ct));

        [HttpPost]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create([FromBody] VoidReasonCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateReasonAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, [FromBody] VoidReasonUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateReasonAsync(id, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteReasonAsync(id, ct) ? NoContent() : NotFound();
    }
}
