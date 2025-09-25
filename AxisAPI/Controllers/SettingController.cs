using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/setting")]
    public class SettingController : ControllerBase
    {
        private readonly ISettingService _settingService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SettingController(ISettingService settingService, IHttpContextAccessor httpContextAccessor)
        {
            _settingService = settingService;
            _httpContextAccessor = httpContextAccessor;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var setting = await _settingService.GetAsync(id, ct);
            if (setting is null) return NotFound();
            return Ok(setting);
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var settings = await _settingService.ListAsync(pagination, ct);
            return Ok(settings);
        }

        [Authorize]
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, SettingUpdateDto dto, CancellationToken ct)
        {
            var updatedBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var success = await _settingService.UpdateAsync(id, dto, updatedBy, ct);
            if (!success) return NotFound();
            return NoContent();
        }
        [Authorize]
        [HttpPost]
        public async Task<IActionResult> Create(SettingCreateDto dto, CancellationToken ct)
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var created = await _settingService.CreateAsync(dto, createdBy, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var success = await _settingService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
