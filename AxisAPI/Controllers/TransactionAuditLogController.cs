using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/transaction-audit-logs")]
    [Authorize(Roles = "admin")]
    public class TransactionAuditLogController : ControllerBase
    {
        private readonly ITransactionAuditLogService _svc;
        public TransactionAuditLogController(ITransactionAuditLogService svc) => _svc = svc;

        // GET /api/transaction-audit-logs/{transactionId}
        [HttpGet("{transactionId:int}")]
        public async Task<IActionResult> GetByTransaction(int transactionId, CancellationToken ct)
        {
            var logs = await _svc.GetByTransaction(transactionId, ct);
            return Ok(logs);
        }

        // GET /api/transaction-audit-logs?page=1&pageSize=50
        [HttpGet]
        public async Task<IActionResult> ListAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            var result = await _svc.ListAll(page, pageSize, ct);
            return Ok(result);
        }

        // No POST, PUT, DELETE — intentionally omitted
    }
}