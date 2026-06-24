using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/ai/actions")]
    [Authorize(Roles = "admin")]
    public class PendingActionsController : ControllerBase
    {
        private readonly IPendingActionService _svc;
        public PendingActionsController(IPendingActionService svc) => _svc = svc;

        // GET /api/ai/actions?status=Pending&page=1&pageSize=50
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? status = "Pending",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
            => Ok(await _svc.ListAsync(new PendingActionsFilterDto(status, page, pageSize), ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var r = await _svc.GetAsync(id, ct);
            return r is null ? NotFound() : Ok(r);
        }

        [HttpPost("{id:int}/approve")]
        public async Task<IActionResult> Approve(int id, CancellationToken ct)
        {
            var actor = User?.Identity?.Name ?? "admin";
            return Ok(await _svc.ApproveAsync(id, actor, ct));
        }

        public record RejectBody(string? Note);

        [HttpPost("{id:int}/reject")]
        public async Task<IActionResult> Reject(int id, [FromBody] RejectBody? body, CancellationToken ct)
        {
            var actor = User?.Identity?.Name ?? "admin";
            return Ok(await _svc.RejectAsync(id, actor, body?.Note, ct));
        }
    }
}
