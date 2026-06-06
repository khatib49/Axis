using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "admin")] // Only admins can manage Chart of Accounts
    public class AccountsController : ControllerBase
    {
        private readonly IAccountService _accountService;
        private readonly ILogger<AccountsController> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AccountsController(
            IAccountService accountService,
            ILogger<AccountsController> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _accountService = accountService;
            _logger = logger;
            _httpContextAccessor = httpContextAccessor;
        }

        private int? GetCurrentUserId()
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("sub")?.Value
                           ?? _httpContextAccessor.HttpContext?.User?.FindFirst("id")?.Value;
            return int.TryParse(userIdClaim, out var userId) ? userId : null;
        }

        // ============================================
        // ACCOUNT TYPE ENDPOINTS
        // ============================================

        /// <summary>
        /// Get all account types (Asset, Liability, Equity, Revenue, Expense)
        /// </summary>
        [HttpGet("types")]
        public async Task<IActionResult> GetAccountTypes(CancellationToken ct)
        {
            var result = await _accountService.GetAllAccountTypesAsync(ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Get account type by ID
        /// </summary>
        [HttpGet("types/{id:int}")]
        public async Task<IActionResult> GetAccountTypeById(int id, CancellationToken ct)
        {
            var result = await _accountService.GetAccountTypeByIdAsync(id, ct);
            return result.Success ? Ok(result) : NotFound(result);
        }

        // ============================================
        // ACCOUNT CRUD ENDPOINTS
        // ============================================

        /// <summary>
        /// Create a new account
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateAccount([FromBody] AccountCreateDto dto, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            var result = await _accountService.CreateAccountAsync(dto, userId, ct);

            if (result.Success)
                return CreatedAtAction(nameof(GetAccountById), new { id = result.Data!.Id }, result);

            return BadRequest(result);
        }

        /// <summary>
        /// Update existing account
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateAccount(int id, [FromBody] AccountUpdateDto dto, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            var result = await _accountService.UpdateAccountAsync(id, dto, userId, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Deactivate account (soft delete)
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeactivateAccount(int id, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            var result = await _accountService.DeactivateAccountAsync(id, userId, ct);
            return result.Success ? NoContent() : BadRequest(result);
        }

        /// <summary>
        /// Get account by ID
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetAccountById(int id, CancellationToken ct)
        {
            var result = await _accountService.GetAccountByIdAsync(id, ct);
            return result.Success ? Ok(result) : NotFound(result);
        }

        /// <summary>
        /// Get account by account number
        /// </summary>
        [HttpGet("by-number/{accountNumber}")]
        public async Task<IActionResult> GetAccountByNumber(string accountNumber, CancellationToken ct)
        {
            var result = await _accountService.GetAccountByNumberAsync(accountNumber, ct);
            return result.Success ? Ok(result) : NotFound(result);
        }

        /// <summary>
        /// Get all accounts with optional filters
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllAccounts(
            [FromQuery] int? accountTypeId,
            [FromQuery] bool? isActive,
            CancellationToken ct)
        {
            var result = await _accountService.GetAllAccountsAsync(accountTypeId, isActive, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Search accounts with pagination
        /// </summary>
        [HttpPost("search")]
        public async Task<IActionResult> SearchAccounts([FromBody] AccountSearchDto searchDto, CancellationToken ct)
        {
            var result = await _accountService.SearchAccountsAsync(searchDto, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Get account hierarchy (tree structure)
        /// </summary>
        [HttpGet("hierarchy")]
        public async Task<IActionResult> GetAccountHierarchy(
            [FromQuery] int? accountTypeId,
            CancellationToken ct)
        {
            var result = await _accountService.GetAccountHierarchyAsync(accountTypeId, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        // ============================================
        // BALANCE & REPORTING ENDPOINTS
        // ============================================

        /// <summary>
        /// Get current balance for specific account
        /// </summary>
        [HttpGet("{id:int}/balance")]
        public async Task<IActionResult> GetAccountBalance(int id, CancellationToken ct)
        {
            var result = await _accountService.GetAccountBalanceAsync(id, ct);
            return result.Success ? Ok(result) : NotFound(result);
        }

        /// <summary>
        /// Get balances for all accounts
        /// </summary>
        [HttpGet("balances")]
        public async Task<IActionResult> GetAllAccountBalances(CancellationToken ct)
        {
            var result = await _accountService.GetAllAccountBalancesAsync(ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Get account summary grouped by type
        /// </summary>
        [HttpGet("summary")]
        public async Task<IActionResult> GetAccountSummary(CancellationToken ct)
        {
            var result = await _accountService.GetAccountSummaryAsync(ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Generate trial balance report
        /// </summary>
        [HttpGet("trial-balance")]
        public async Task<IActionResult> GetTrialBalance(
            [FromQuery] DateTime? asOfDate,
            CancellationToken ct)
        {
            var result = await _accountService.GetTrialBalanceAsync(asOfDate, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Get general ledger for specific account
        /// </summary>
        [HttpGet("{id:int}/general-ledger")]
        public async Task<IActionResult> GetGeneralLedger(
            int id,
            [FromQuery] DateTime? fromDate,
            [FromQuery] DateTime? toDate,
            CancellationToken ct)
        {
            var result = await _accountService.GetGeneralLedgerAsync(id, fromDate, toDate, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        // ============================================
        // VALIDATION ENDPOINTS
        // ============================================

        /// <summary>
        /// Validate account before creation/update
        /// </summary>
        [HttpPost("validate")]
        public async Task<IActionResult> ValidateAccount([FromBody] AccountCreateDto dto, CancellationToken ct)
        {
            var validation = await _accountService.ValidateAccountAsync(
                dto.AccountNumber,
                dto.AccountName,
                dto.AccountTypeId,
                dto.ParentAccountId,
                null,
                ct);

            return Ok(validation);
        }
        [HttpGet("expense-accounts")]
        public async Task<IActionResult> GetExpenseAccounts(CancellationToken ct)
        {
            try
            {
                var accounts = await _accountService.GetExpenseAccountsAsync(ct);
                return Ok(accounts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting expense accounts");
                return StatusCode(500, "Error getting expense accounts");
            }
        }

        /// <summary>
        /// Returns every active account that allows manual entry, with its account
        /// type. Used by the Expense Categories form so a category can be mapped to
        /// any account (e.g. an Equity account for owner draws, or a Revenue
        /// account for manual revenue entries), not just 5xxx expense accounts.
        /// </summary>
        [HttpGet("postable")]
        public async Task<IActionResult> GetPostableAccounts(CancellationToken ct)
        {
            try
            {
                var accounts = await _accountService.GetPostableAccountsAsync(ct);
                return Ok(accounts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting postable accounts");
                return StatusCode(500, "Error getting postable accounts");
            }
        }

        /// <summary>
        /// Bulk re-classifies one or more journal-entry lines onto NewAccountId.
        /// Used by the "Transactions Report" modal on the Chart of Accounts so
        /// an admin can checkbox-select lines and move them to the correct
        /// account without writing SQL. Skips voided entries and lines already
        /// on the target. Adjusts CurrentBalance for posted lines.
        /// Admin-only.
        /// </summary>
        [HttpPost("repoint-lines")]
        public async Task<IActionResult> RepointLines([FromBody] RepointLinesRequestDto dto, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            var result = await _accountService.RepointJournalEntryLinesAsync(dto, userId, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}