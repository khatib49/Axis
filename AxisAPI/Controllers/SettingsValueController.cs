using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/settingsvalue")]
    public class SettingsValueController : ControllerBase
    {
        private readonly ISettingsValueService _settingsValueService;

        public SettingsValueController(ISettingsValueService settingsValueService)
        {
            _settingsValueService = settingsValueService;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var settingsValue = await _settingsValueService.GetAsync(id, ct);
            if (settingsValue is null) return NotFound();
            return Ok(settingsValue);
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var settingsValues = await _settingsValueService.ListAsync(ct);
            return Ok(settingsValues);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, SettingsValueUpdateDto dto, CancellationToken ct)
        {
            var success = await _settingsValueService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> Create(SettingsValueCreateDto dto, CancellationToken ct)
        {
            var created = await _settingsValueService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var success = await _settingsValueService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
