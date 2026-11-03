using Application.DTOs;
using Application.IServices;
using Application.Services.Bi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Bi
{
    [ApiController]
    [Route("api/bi/reports")]
    [Authorize(Roles = "admin")]
    public class BiReportsController : ControllerBase
    {
        private readonly IScheduledReportService _svc;
        public BiReportsController(IScheduledReportService svc) => _svc = svc;

        [HttpGet("types")]
        public IActionResult Types() => Ok(BiReportService.ReportTypes);

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var r = await _svc.GetAsync(id, ct);
            return r is null ? NotFound() : Ok(r);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ScheduledReportCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? CreatedAtAction(nameof(Get), new { id = r.Data!.Id }, r.Data) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ScheduledReportUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            if (r.Success) return Ok(r.Data);
            return r.Message == "Not found" ? NotFound(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
            await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound();

        [HttpGet("{id:int}/runs")]
        public async Task<IActionResult> Runs(int id, [FromQuery] int take = 50, CancellationToken ct = default) =>
            Ok(await _svc.GetRunsAsync(id, take, ct));

        /// <summary>Build + send now. ?day=yyyy-MM-dd (default today).</summary>
        [HttpPost("{id:int}/run")]
        public async Task<IActionResult> RunNow(int id, [FromQuery] DateTime? day, CancellationToken ct)
        {
            var r = await _svc.RunNowAsync(id, day, ct);
            return r.Success ? Ok(r.Data) : NotFound(r);
        }

        /// <summary>Render without sending.</summary>
        [HttpGet("{id:int}/preview")]
        public async Task<IActionResult> Preview(int id, [FromQuery] DateTime? day, CancellationToken ct)
        {
            var r = await _svc.PreviewAsync(id, day, ct);
            if (r.Success) return Ok(r.Data);
            return r.Message == "Not found" ? NotFound(r) : BadRequest(r);
        }
    }
}
