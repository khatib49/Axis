using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.OrderingChannels
{
    /// <summary>Admin: QR tables + their public URLs / QR payloads.</summary>
    [ApiController]
    [Route("api/selforder/tables")]
    [Authorize(Roles = "admin")]
    public class SelfOrderTablesController : ControllerBase
    {
        private readonly ISelfOrderService _svc;
        public SelfOrderTablesController(ISelfOrderService svc) { _svc = svc; }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListTablesAsync(ct));

        /// <summary>
        /// Public URL + QR payload per table. Base URL: ?baseUrl= query, else the
        /// request's Origin header, else IntegrationSettings "SelfOrder.PublicBaseUrl".
        /// </summary>
        [HttpGet("qr")]
        public async Task<IActionResult> Qr([FromQuery] string? baseUrl, CancellationToken ct)
        {
            var origin = string.IsNullOrWhiteSpace(baseUrl) ? Request.Headers.Origin.FirstOrDefault() : baseUrl;
            return Ok(await _svc.ListTableQrsAsync(origin, ct));
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SelfOrderTableUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateTableAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SelfOrderTableUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateTableAsync(id, dto, ct);
            if (!r.Success && r.Error == "NotFound") return NotFound(r);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => await _svc.DeleteTableAsync(id, ct) ? NoContent() : NotFound();
    }
}
