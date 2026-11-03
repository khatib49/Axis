using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.AccountingPlus
{
    [ApiController]
    [Route("api/accountingplus/periods")]
    [Authorize(Roles = "admin")]
    public class AccountingPeriodsController : ControllerBase
    {
        private readonly IAccountingPeriodService _svc;
        public AccountingPeriodsController(IAccountingPeriodService svc) => _svc = svc;

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int? year, CancellationToken ct)
            => Ok(await _svc.ListAsync(year ?? DateTime.UtcNow.Year, ct));

        [HttpGet("check")]
        public async Task<IActionResult> Check([FromQuery] DateTime date, CancellationToken ct)
            => Ok(await _svc.EnsureOpenAsync(date, ct));

        [HttpPost("lock")]
        public async Task<IActionResult> Lock([FromBody] PeriodLockRequestDto dto, CancellationToken ct)
        {
            var r = await _svc.LockAsync(dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpPost("unlock")]
        public async Task<IActionResult> Unlock([FromBody] PeriodLockRequestDto dto, CancellationToken ct)
        {
            var r = await _svc.UnlockAsync(dto.Year, dto.Month, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }
    }
}
