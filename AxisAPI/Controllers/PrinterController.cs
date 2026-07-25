using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class PrinterController : ControllerBase
    {
        private readonly IPrinterService _service;

        public PrinterController(IPrinterService service) => _service = service;

        [HttpGet("{id:int}")]
        public async Task<ActionResult<PrinterDto>> Get(int id, CancellationToken ct)
        {
            var dto = await _service.GetAsync(id, ct);
            return dto is null ? NotFound() : Ok(dto);
        }

        [HttpGet]
        public async Task<ActionResult<List<PrinterDto>>> List([FromQuery] PrinterListFilterDto filter, CancellationToken ct)
        {
            var res = await _service.ListAsync(filter, ct);
            return Ok(res);
        }

        [HttpPost]
        public async Task<ActionResult<PrinterDto>> Create([FromBody] PrinterCreateDto dto, CancellationToken ct)
        {
            try
            {
                var created = await _service.CreateAsync(dto, ct);
                return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] PrinterUpdateDto dto, CancellationToken ct)
        {
            try
            {
                var ok = await _service.UpdateAsync(id, dto, ct);
                return ok ? NoContent() : NotFound();
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var ok = await _service.DeleteAsync(id, ct);
            return ok ? NoContent() : NotFound();
        }

        /// <summary>Sends a test ticket to the printer through the on-site agent.</summary>
        [HttpPost("{id:int}/test")]
        public async Task<IActionResult> TestPrint(int id, CancellationToken ct)
        {
            var ok = await _service.TestPrintAsync(id, ct);
            return ok
                ? Ok(new { message = "Test ticket dispatched. If the agent is online it will print shortly." })
                : NotFound();
        }
    }
}
