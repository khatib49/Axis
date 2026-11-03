using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.AccountingPlus
{
    [ApiController]
    [Route("api/accountingplus/tax")]
    [Authorize(Roles = "admin")]
    public class TaxController : ControllerBase
    {
        private readonly ITaxService _svc;
        public TaxController(ITaxService svc) => _svc = svc;

        [HttpGet("rates")]
        public async Task<IActionResult> ListRates([FromQuery] bool includeInactive = false, CancellationToken ct = default)
            => Ok(await _svc.ListRatesAsync(includeInactive, ct));

        [HttpPost("rates")]
        public async Task<IActionResult> CreateRate([FromBody] TaxRateUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateRateAsync(dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpPut("rates/{id:int}")]
        public async Task<IActionResult> UpdateRate(int id, [FromBody] TaxRateUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateRateAsync(id, dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpDelete("rates/{id:int}")]
        public async Task<IActionResult> DeleteRate(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteRateAsync(id, ct);
            return r.Success ? NoContent() : BadRequest(r);
        }

        /// <summary>VAT report per month. Prices are tax-inclusive by default (inclusive=true).</summary>
        [HttpGet("vat-report")]
        public async Task<IActionResult> VatReport([FromQuery] DateTime from, [FromQuery] DateTime to, [FromQuery] bool inclusive = true, CancellationToken ct = default)
        {
            var r = await _svc.GetVatReportAsync(from, to, inclusive, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }
    }
}
