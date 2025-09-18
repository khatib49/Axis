using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/setting")]
    public class SettingController : ControllerBase
    {
        private readonly ISettingService _settingService;

        public SettingController(ISettingService settingService)
        {
            _settingService = settingService;
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

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, SettingUpdateDto dto, CancellationToken ct)
        {
            var success = await _settingService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> Create(SettingCreateDto dto, CancellationToken ct)
        {
            var created = await _settingService.CreateAsync(dto, ct);
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
