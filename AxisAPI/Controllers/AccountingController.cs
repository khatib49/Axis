using Application.DTOs;
using Application.IServices;
using Hangfire;
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
        /// Kicks off a background backfill: creates missing journal entries for all
        /// completed transactions AND re-points existing entries to their currently
        /// mapped account when the mapping has changed. Returns immediately with a
        /// Hangfire job id; check progress at /hangfire.
        /// </summary>
        [HttpPost("backfill/transactions")]
        public IActionResult BackfillTransactions()
        {
            var jobId = BackgroundJob.Enqueue<IBackfillService>(
                s => s.BackfillTransactionsBgAsync(CancellationToken.None));
            return Accepted(new { jobId, message = "Transaction backfill started in background." });
        }

        /// <summary>
        /// Kicks off a background backfill: creates missing journal entries for all
        /// expenses AND re-points existing entries to their currently mapped account
        /// when the mapping has changed. Returns immediately with a Hangfire job id.
        /// </summary>
        [HttpPost("backfill/expenses")]
        public IActionResult BackfillExpenses()
        {
            var jobId = BackgroundJob.Enqueue<IBackfillService>(
                s => s.BackfillExpensesBgAsync(CancellationToken.None));
            return Accepted(new { jobId, message = "Expense backfill started in background." });
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

        /// <summary>
        /// Revenue coverage audit. Shows whether the chart of accounts revenue
        /// side matches the calculator (sum of TransactionRecord.TotalPrice).
        /// Returns orphan transaction ids that have no journal entry, plus a
        /// before/after style discrepancy figure. Pair this with the
        /// /Accounting/backfill/transactions endpoint to close the gap.
        /// </summary>
        [HttpGet("audit-revenue-coverage")]
        public async Task<ActionResult<RevenueCoverageAuditDto>> AuditRevenueCoverage(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct = default)
        {
            var result = await _svc.GetRevenueCoverageAuditAsync(from, to, ct);
            return Ok(result);
        }
    }
}
