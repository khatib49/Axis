using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.TillControls
{
    [ApiController]
    [Route("api/till/tenders")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class TendersController : ControllerBase
    {
        private readonly ITenderService _svc;
        public TendersController(ITenderService svc) => _svc = svc;

        /// <summary>Add one or more tenders (currency + method) to a transaction; returns the updated summary.</summary>
        [HttpPost]
        public async Task<IActionResult> Add([FromBody] AddTendersDto dto, CancellationToken ct)
        {
            var r = await _svc.AddAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        /// <summary>Paid vs total, remaining, and change due in each active currency.</summary>
        [HttpGet("transaction/{transactionId:int}")]
        public async Task<IActionResult> Summary(int transactionId, CancellationToken ct)
        {
            var r = await _svc.SummaryAsync(transactionId, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        [HttpGet("shift/{shiftId:int}")]
        public async Task<IActionResult> ByShift(int shiftId, CancellationToken ct)
            => Ok(await _svc.ListByShiftAsync(shiftId, ct));

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteAsync(id, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
