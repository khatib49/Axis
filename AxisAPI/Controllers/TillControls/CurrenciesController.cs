using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.TillControls
{
    [ApiController]
    [Route("api/currency")]
    [Authorize]
    public class CurrenciesController : ControllerBase
    {
        private readonly ICurrencyService _svc;
        public CurrenciesController(ICurrencyService svc) => _svc = svc;

        /// <summary>Currencies with their current rate (seeds USD/LBP on first call).</summary>
        [HttpGet("currencies")]
        public async Task<IActionResult> Currencies([FromQuery] bool includeInactive, CancellationToken ct)
            => Ok(await _svc.ListAsync(includeInactive, ct));

        [HttpGet("currencies/{code}")]
        public async Task<IActionResult> Currency(string code, CancellationToken ct)
        {
            var c = await _svc.GetAsync(code, ct);
            return c is null ? NotFound() : Ok(c);
        }

        [HttpPost("currencies")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> CreateCurrency([FromBody] CurrencyCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpPut("currencies/{code}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> UpdateCurrency(string code, [FromBody] CurrencyUpdateDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateAsync(code, dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        /// <summary>Current (latest effective) rate per active currency.</summary>
        [HttpGet("rates")]
        public async Task<IActionResult> Rates(CancellationToken ct) => Ok(await _svc.ListAsync(false, ct));

        /// <summary>Set today's rate (or a rate effective from a given date).</summary>
        [HttpPost("rates")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> SetRate([FromBody] SetRateDto dto, CancellationToken ct)
        {
            var r = await _svc.SetRateAsync(dto, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }

        [HttpGet("rates/{code}/history")]
        public async Task<IActionResult> History(string code, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
            => Ok(await _svc.HistoryAsync(code, from, to, ct));

        /// <summary>Convert an amount between two currencies at the latest effective rates, rounded to the target's cash step.</summary>
        [HttpGet("convert")]
        public async Task<IActionResult> Convert([FromQuery] decimal amount, [FromQuery] string from, [FromQuery] string to, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
                return BadRequest(new BaseResponse<ConversionResultDto>(false, "from and to are required.", "from and to are required."));
            var r = await _svc.ConvertAsync(amount, from, to, ct);
            return r.Success ? Ok(r) : BadRequest(r);
        }
    }
}
