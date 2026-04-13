using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "admin")]
    public class AccountingController : ControllerBase
    {
        private readonly IAccountingReportService _svc;
        private readonly IBackfillService _backfillSvc;

        public AccountingController(IAccountingReportService svc, IBackfillService backfillSvc)
        {
            _svc = svc;
            _backfillSvc = backfillSvc;
        }

        /// <summary>
        /// One-time backfill: creates missing journal entries for all completed transactions.
        /// </summary>
        [HttpPost("backfill/transactions")]
        public async Task<ActionResult<BackfillResultDto>> BackfillTransactions(CancellationToken ct)
        {
            var result = await _backfillSvc.BackfillTransactionsAsync(ct);
            return Ok(result);
        }

        /// <summary>
        /// One-time backfill: creates missing journal entries for all expenses.
        /// </summary>
        [HttpPost("backfill/expenses")]
        public async Task<ActionResult<BackfillResultDto>> BackfillExpenses(CancellationToken ct)
        {
            var result = await _backfillSvc.BackfillExpensesAsync(ct);
            return Ok(result);
        }

        /// <summary>
        /// Full accounting dashboard: revenue breakdown, operating expenses,
        /// capital expenses, COGS, gross profit, net income.
        /// </summary>
        [HttpGet("dashboard")]
        public async Task<ActionResult<AccountingDashboardDto>> GetDashboard([FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
        {
            var result = await _svc.GetDashboardAsync(from, to, ct);
            return Ok(result);
        }

        /// <summary>
        /// Expense lines grouped by category.
        /// Pass capitalOnly=true for capital investments, false for operating.
        /// </summary>
        [HttpGet("expenses-breakdown")]
        public async Task<ActionResult<List<ExpenseCategoryLineDto>>> GetExpensesBreakdown([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] bool capitalOnly = false,
            CancellationToken ct = default)
        {
            var result = await _svc.GetExpensesBreakdownAsync(from, to, capitalOnly, ct);
            return Ok(result);
        }
    }
}
