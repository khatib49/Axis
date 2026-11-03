using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.AccountingPlus
{
    [ApiController]
    [Route("api/accountingplus/receivables")]
    [Authorize(Roles = "admin")]
    public class ReceivablesController : ControllerBase
    {
        private readonly IReceivablesService _svc;
        public ReceivablesController(IReceivablesService svc) => _svc = svc;

        /// <summary>AR aging over open invoices (status 7), read-only.</summary>
        [HttpGet("aging")]
        public async Task<IActionResult> Aging([FromQuery] DateTime? asOf, CancellationToken ct)
            => Ok(await _svc.GetArAgingAsync(asOf, ct));
    }
}
