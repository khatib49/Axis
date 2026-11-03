using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.AccountingPlus
{
    [ApiController]
    [Route("api/accountingplus/bank")]
    [Authorize(Roles = "admin")]
    public class BankReconciliationController : ControllerBase
    {
        private readonly IBankReconciliationService _svc;
        public BankReconciliationController(IBankReconciliationService svc) => _svc = svc;

        // ---- bank accounts ----
        [HttpGet("accounts")]
        public async Task<IActionResult> ListAccounts([FromQuery] bool includeInactive = false, CancellationToken ct = default)
            => Ok(await _svc.ListBankAccountsAsync(includeInactive, ct));

        [HttpGet("accounts/{id:int}")]
        public async Task<IActionResult> GetAccount(int id, CancellationToken ct)
        {
            var r = await _svc.GetBankAccountAsync(id, ct);
            return r.Success ? Ok(r.Data) : NotFound(r);
        }

        [HttpPost("accounts")]
        public async Task<IActionResult> CreateAccount([FromBody] BankAccountUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateBankAccountAsync(dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpPut("accounts/{id:int}")]
        public async Task<IActionResult> UpdateAccount(int id, [FromBody] BankAccountUpsertDto dto, CancellationToken ct)
        {
            var r = await _svc.UpdateBankAccountAsync(id, dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpDelete("accounts/{id:int}")]
        public async Task<IActionResult> DeleteAccount(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteBankAccountAsync(id, ct);
            return r.Success ? NoContent() : BadRequest(r);
        }

        // ---- statements ----
        [HttpGet("statements")]
        public async Task<IActionResult> ListStatements([FromQuery] int? bankAccountId, CancellationToken ct)
            => Ok(await _svc.ListStatementsAsync(bankAccountId, ct));

        [HttpPost("statements")]
        public async Task<IActionResult> CreateStatement([FromBody] BankStatementCreateDto dto, CancellationToken ct)
        {
            var r = await _svc.CreateStatementAsync(dto, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        /// <summary>multipart/form-data: file (text/csv with date,description,reference,amount) + statement header fields.</summary>
        [HttpPost("statements/import")]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> ImportCsv(
            [FromForm] IFormFile file,
            [FromForm] int bankAccountId,
            [FromForm] DateTime periodFrom,
            [FromForm] DateTime periodTo,
            [FromForm] decimal openingBalance,
            [FromForm] decimal closingBalance,
            CancellationToken ct)
        {
            if (file is null || file.Length == 0) return BadRequest(new BaseResponse(false, "CSV file is required"));
            string content;
            using (var reader = new StreamReader(file.OpenReadStream()))
                content = await reader.ReadToEndAsync(ct);
            var r = await _svc.ImportCsvAsync(new BankStatementCreateDto(bankAccountId, periodFrom, periodTo, openingBalance, closingBalance, null), content, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpGet("statements/{id:int}")]
        public async Task<IActionResult> GetStatement(int id, [FromQuery] string? matchStatus, CancellationToken ct)
        {
            var r = await _svc.GetStatementAsync(id, matchStatus, ct);
            return r.Success ? Ok(r.Data) : NotFound(r);
        }

        [HttpDelete("statements/{id:int}")]
        public async Task<IActionResult> DeleteStatement(int id, CancellationToken ct)
        {
            var r = await _svc.DeleteStatementAsync(id, ct);
            return r.Success ? NoContent() : BadRequest(r);
        }

        // ---- matching ----
        [HttpGet("statements/{id:int}/lines/{lineId:int}/suggestions")]
        public async Task<IActionResult> Suggestions(int id, int lineId, CancellationToken ct)
        {
            var r = await _svc.GetSuggestionsAsync(id, lineId, ct);
            return r.Success ? Ok(r.Data) : NotFound(r);
        }

        [HttpPost("statements/{id:int}/lines/{lineId:int}/match")]
        public async Task<IActionResult> Match(int id, int lineId, [FromBody] BankMatchRequestDto dto, CancellationToken ct)
        {
            var r = await _svc.MatchLineAsync(id, lineId, dto.JournalEntryLineId, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpPost("statements/{id:int}/lines/{lineId:int}/unmatch")]
        public async Task<IActionResult> Unmatch(int id, int lineId, CancellationToken ct)
        {
            var r = await _svc.UnmatchLineAsync(id, lineId, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpPost("statements/{id:int}/lines/{lineId:int}/exclude")]
        public async Task<IActionResult> Exclude(int id, int lineId, [FromBody] BankExcludeRequestDto? dto, CancellationToken ct)
        {
            var r = await _svc.ExcludeLineAsync(id, lineId, dto?.Note, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }

        [HttpGet("statements/{id:int}/summary")]
        public async Task<IActionResult> Summary(int id, CancellationToken ct)
        {
            var r = await _svc.GetSummaryAsync(id, ct);
            return r.Success ? Ok(r.Data) : NotFound(r);
        }

        [HttpPost("statements/{id:int}/reconcile")]
        public async Task<IActionResult> Reconcile(int id, CancellationToken ct)
        {
            var r = await _svc.ReconcileAsync(id, ct);
            return r.Success ? Ok(r.Data) : BadRequest(r);
        }
    }
}
