using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.AccountingPlus
{
    [ApiController]
    [Route("api/accountingplus/bills")]
    [Authorize(Roles = "admin")]
    public class SupplierBillsController : ControllerBase
    {
        private readonly ISupplierBillService _svc;
        public SupplierBillsController(ISupplierBillService svc) => _svc = svc;

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] SupplierBillFilterDto filter, CancellationToken ct)
            => Ok(await _svc.ListAsync(filter, ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var r = await _svc.GetAsync(id, ct);
            return r.Success ? Ok(r.Data) : NotFound(r);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SupplierBillCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateDraft(int id, [FromBody] SupplierBillUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateDraftAsync(id, dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpPost("{id:int}/open")]
        public async Task<IActionResult> Open(int id, CancellationToken ct)
        {
            var r = await _svc.OpenAsync(id, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpPost("{id:int}/payments")]
        public async Task<IActionResult> AddPayment(int id, [FromBody] SupplierBillPaymentCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.AddPaymentAsync(id, dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpPost("{id:int}/void")]
        public async Task<IActionResult> Void(int id, [FromBody] VoidRequestDto? dto, CancellationToken ct)
        {
            var r = await _svc.VoidAsync(id, dto?.Reason, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteDraft(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteDraftAsync(id, ct);
            return r.Success ? NoContent() : BadRequest(r);
        }

        [HttpGet("aging")]
        public async Task<IActionResult> Aging([FromQuery] DateTime? asOf, CancellationToken ct)
            => Ok(await _svc.GetApAgingAsync(asOf, ct));

        [HttpGet("due-soon")]
        public async Task<IActionResult> DueSoon([FromQuery] int days = 7, CancellationToken ct = default)
            => Ok(await _svc.GetDueSoonAsync(days, ct));
    }
}
