using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "admin")] // Only admins can manage journal entries
    public class JournalController : ControllerBase
    {
        private readonly IJournalService _journalService;
        private readonly ILogger<JournalController> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public JournalController(
            IJournalService journalService,
            ILogger<JournalController> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _journalService = journalService;
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
        // JOURNAL ENTRY CRUD ENDPOINTS
        // ============================================

        /// <summary>
        /// Create a new journal entry (unposted)
        /// Validates that debits equal credits
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateJournalEntry([FromBody] JournalEntryCreateDto dto, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            var result = await _journalService.CreateJournalEntryAsync(dto, userId, ct);

            if (result.Success)
                return CreatedAtAction(nameof(GetJournalEntryById), new { id = result.Data!.Id }, result);

            return BadRequest(result);
        }

        /// <summary>
        /// Update an unposted journal entry
        /// Cannot update posted entries
        /// </summary>
        [HttpPut("{id:int}")]
        public async Task<IActionResult> UpdateJournalEntry(int id, [FromBody] JournalEntryUpdateDto dto, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            var result = await _journalService.UpdateJournalEntryAsync(id, dto, userId, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Post journal entry to ledger
        /// Updates account balances and locks entry from editing
        /// </summary>
        [HttpPost("{id:int}/post")]
        public async Task<IActionResult> PostJournalEntry(int id, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            var result = await _journalService.PostJournalEntryAsync(id, userId, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Void/reverse a posted journal entry
        /// Creates a reversing entry instead of deleting
        /// </summary>
        [HttpPost("{id:int}/void")]
        public async Task<IActionResult> VoidJournalEntry(int id, [FromBody] VoidJournalEntryDto dto, CancellationToken ct)
        {
            var userId = GetCurrentUserId();
            var result = await _journalService.VoidJournalEntryAsync(id, dto.VoidReason, userId, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Delete an unposted journal entry
        /// Cannot delete posted entries
        /// </summary>
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteJournalEntry(int id, CancellationToken ct)
        {
            var result = await _journalService.DeleteJournalEntryAsync(id, ct);
            return result.Success ? NoContent() : BadRequest(result);
        }

        // ============================================
        // QUERY ENDPOINTS
        // ============================================

        /// <summary>
        /// Get journal entry by ID with all lines
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetJournalEntryById(int id, CancellationToken ct)
        {
            var result = await _journalService.GetJournalEntryByIdAsync(id, ct);
            return result.Success ? Ok(result) : NotFound(result);
        }

        /// <summary>
        /// Get journal entry by entry number
        /// </summary>
        [HttpGet("by-number/{entryNumber}")]
        public async Task<IActionResult> GetJournalEntryByNumber(string entryNumber, CancellationToken ct)
        {
            var result = await _journalService.GetJournalEntryByNumberAsync(entryNumber, ct);
            return result.Success ? Ok(result) : NotFound(result);
        }

        /// <summary>
        /// Search journal entries with filters and pagination
        /// </summary>
        [HttpPost("search")]
        public async Task<IActionResult> SearchJournalEntries([FromBody] JournalEntrySearchDto searchDto, CancellationToken ct)
        {
            var result = await _journalService.SearchJournalEntriesAsync(searchDto, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// Get all journal entries for a specific reference (e.g., all entries for Transaction #123)
        /// </summary>
        [HttpGet("by-reference/{referenceType}/{referenceId:int}")]
        public async Task<IActionResult> GetJournalEntriesByReference(string referenceType, int referenceId, CancellationToken ct)
        {
            var result = await _journalService.GetJournalEntriesByReferenceAsync(referenceType, referenceId, ct);
            return result.Success ? Ok(result) : NotFound(result);
        }

        // ============================================
        // AUTOMATED JOURNAL ENTRY ENDPOINTS
        // ============================================

        /// <summary>
        /// Create journal entry from transaction (gaming or FNB sale)
        /// </summary>
        [HttpPost("from-transaction/{transactionId:int}")]
        public async Task<IActionResult> CreateJournalEntryFromTransaction(int transactionId, CancellationToken ct)
        {
            var result = await _journalService.CreateJournalEntryFromTransactionAsync(transactionId, ct);

            if (result.Success)
                return CreatedAtAction(nameof(GetJournalEntryById), new { id = result.Data!.Id }, result);

            return BadRequest(result);
        }

        /// <summary>
        /// Create journal entry from expense
        /// </summary>
        [HttpPost("from-expense/{expenseId:int}")]
        public async Task<IActionResult> CreateJournalEntryFromExpense(int expenseId, CancellationToken ct)
        {
            var result = await _journalService.CreateJournalEntryFromExpenseAsync(expenseId, ct);

            if (result.Success)
                return CreatedAtAction(nameof(GetJournalEntryById), new { id = result.Data!.Id }, result);

            return BadRequest(result);
        }

        // ============================================
        // VALIDATION ENDPOINTS
        // ============================================

        /// <summary>
        /// Validate journal entry before creation/posting
        /// Checks debits = credits, accounts exist, etc.
        /// </summary>
        [HttpPost("validate")]
        public async Task<IActionResult> ValidateJournalEntry([FromBody] JournalEntryCreateDto dto, CancellationToken ct)
        {
            var validation = await _journalService.ValidateJournalEntryAsync(dto, ct);
            return Ok(validation);
        }

        /// <summary>
        /// Check if journal entry can be posted
        /// </summary>
        [HttpGet("{id:int}/can-post")]
        public async Task<IActionResult> CanPostJournalEntry(int id, CancellationToken ct)
        {
            var result = await _journalService.CanPostJournalEntryAsync(id, ct);
            return Ok(result);
        }

        /// <summary>
        /// Check if journal entry can be voided
        /// </summary>
        [HttpGet("{id:int}/can-void")]
        public async Task<IActionResult> CanVoidJournalEntry(int id, CancellationToken ct)
        {
            var result = await _journalService.CanVoidJournalEntryAsync(id, ct);
            return Ok(result);
        }

        // ============================================
        // UTILITY ENDPOINTS
        // ============================================

        /// <summary>
        /// Generate next entry number
        /// Format: JE-{YEAR}-{SEQUENCE} (e.g., JE-2024-00123)
        /// </summary>
        [HttpGet("next-entry-number")]
        public async Task<IActionResult> GenerateEntryNumber(CancellationToken ct)
        {
            var entryNumber = await _journalService.GenerateEntryNumberAsync(ct);
            return Ok(new { entryNumber });
        }

        /// <summary>
        /// Recalculate all account balances from journal entries
        /// Used for data integrity checks and corrections
        /// </summary>
        [HttpPost("recalculate-balances")]
        public async Task<IActionResult> RecalculateAccountBalances(CancellationToken ct)
        {
            _logger.LogWarning("Manual account balance recalculation initiated by user");
            var result = await _journalService.RecalculateAccountBalancesAsync(ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }
    }
}