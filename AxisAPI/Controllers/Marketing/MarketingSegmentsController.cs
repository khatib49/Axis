using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Marketing
{
    [ApiController]
    [Route("api/marketing/segments")]
    [Authorize(Roles = "admin")]
    public class MarketingSegmentsController : ControllerBase
    {
        private readonly IMarketingSegmentService _svc;
        public MarketingSegmentsController(IMarketingSegmentService svc) => _svc = svc;

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var s = await _svc.GetAsync(id, ct);
            return s is null ? NotFound() : Ok(s);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SegmentCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? CreatedAtAction(nameof(Get), new { id = r.Data!.Id }, r.Data) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] SegmentUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(id, dto, ct);
            if (r.Success) return Ok(r.Data);
            return r.Message == "Not found" ? NotFound(r) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteAsync(id, ct);
            if (r.Success) return NoContent();
            return r.Message == "Not found" ? NotFound(r) : BadRequest(r);
        }

        /// <summary>Audience of a saved segment: count + first 50 rows.</summary>
        [HttpGet("{id:int}/preview")]
        public async Task<IActionResult> Preview(int id, CancellationToken ct)
        {
            var r = await _svc.PreviewAsync(id, ct);
            return r.Success ? Ok(r.Data) : NotFound(r);
        }

        /// <summary>Audience for ad-hoc rules (live rule builder): count + first 50 rows.</summary>
        [HttpPost("preview")]
        public async Task<IActionResult> PreviewRules([FromBody] SegmentRulesDto rules, CancellationToken ct) =>
            Ok(await _svc.PreviewRulesAsync(rules ?? new SegmentRulesDto(), ct));
    }
}
