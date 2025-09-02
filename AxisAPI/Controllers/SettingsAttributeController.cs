using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/settingsattribute")]
    public class SettingsAttributeController : ControllerBase
    {
        private readonly ISettingsAttributeService _settingsAttributeService;

        public SettingsAttributeController(ISettingsAttributeService settingsAttributeService)
        {
            _settingsAttributeService = settingsAttributeService;
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var settingsAttribute = await _settingsAttributeService.GetAsync(id, ct);
            if (settingsAttribute is null) return NotFound();
            return Ok(settingsAttribute);
        }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
        {
            var settingsAttributes = await _settingsAttributeService.ListAsync(ct);
            return Ok(settingsAttributes);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, SettingsAttributeUpdateDto dto, CancellationToken ct)
        {
            var success = await _settingsAttributeService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [HttpPost]
        public async Task<IActionResult> Create(SettingsAttributeCreateDto dto, CancellationToken ct)
        {
            var created = await _settingsAttributeService.CreateAsync(dto, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var success = await _settingsAttributeService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
