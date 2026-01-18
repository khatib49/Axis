using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class JournalService : IJournalService
    {
        private readonly IBaseRepository<JournalEntry> _journalRepo;
        private readonly IBaseRepository<JournalEntryLine> _journalLineRepo;
        private readonly IBaseRepository<Account> _accountRepo;
        private readonly IBaseRepository<TransactionRecord> _transactionRepo;
        private readonly IBaseRepository<Expense> _expenseRepo;
        private readonly IBaseRepository<Category> _categoryRepo;
        private readonly IUnitOfWork _uow;
        private readonly AccountingMapper _mapper;
        private readonly ILogger<JournalService> _logger;

        public JournalService(
            IBaseRepository<JournalEntry> journalRepo,
            IBaseRepository<JournalEntryLine> lineRepo,
            IBaseRepository<Account> accountRepo,
            IBaseRepository<TransactionRecord> transactionRepo,
            IBaseRepository<Expense> expenseRepo,
            IBaseRepository<Category> categoryRepo,
            IUnitOfWork uow,
            AccountingMapper mapper,
            ILogger<JournalService> logger)
        {
            _journalRepo = journalRepo;
            _journalLineRepo = lineRepo;
            _accountRepo = accountRepo;
            _transactionRepo = transactionRepo;
            _expenseRepo = expenseRepo;
            _categoryRepo = categoryRepo;
            _uow = uow;
            _mapper = mapper;
            _logger = logger;
        }

        // ============================================
        // JOURNAL ENTRY OPERATIONS
        // ============================================

        public async Task<BaseResponse<JournalEntryDto>> CreateJournalEntryAsync(
            JournalEntryCreateDto dto,
            int? createdBy,
            CancellationToken ct = default)
        {
            try
            {
                // Validate
                var validation = await ValidateJournalEntryAsync(dto, ct);
                if (!validation.IsValid)
                    return new BaseResponse<JournalEntryDto>(false, string.Join(", ", validation.Errors), "", null);

                // Generate entry number
                var entryNumber = await GenerateEntryNumberAsync(ct);

                // Create journal entry
                var entry = new JournalEntry
                {
                    EntryNumber = entryNumber,
                    EntryDate = dto.EntryDate,
                    Description = dto.Description.Trim(),
                    ReferenceType = dto.ReferenceType,
                    ReferenceId = dto.ReferenceId,
                    TotalAmount = validation.TotalDebits,
                    IsPosted = false,
                    IsVoided = false,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };

                await _journalRepo.AddAsync(entry, ct);
                await _uow.SaveChangesAsync(ct);

                // Create lines
                var lineNumber = 1;
                foreach (var lineDto in dto.Lines)
                {
                    var line = new JournalEntryLine
                    {
                        JournalEntryId = entry.Id,
                        AccountId = lineDto.AccountId,
                        DebitAmount = lineDto.DebitAmount,
                        CreditAmount = lineDto.CreditAmount,
                        Description = lineDto.Description?.Trim(),
                        LineNumber = lineNumber++,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _journalLineRepo.AddAsync(line, ct);
                }

                await _uow.SaveChangesAsync(ct);

                _logger.LogInformation("Journal entry created: {EntryNumber}", entryNumber);

                // Return with full details
                return await GetJournalEntryByIdAsync(entry.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating journal entry");
                return new BaseResponse<JournalEntryDto>(false, "Error creating journal entry", "", null);
            }
        }

        public async Task<BaseResponse<JournalEntryDto>> UpdateJournalEntryAsync(
            int id,
            JournalEntryUpdateDto dto,
            int? modifiedBy,
            CancellationToken ct = default)
        {
            try
            {
                var entry = await _journalRepo.Query(asNoTracking: false)
                    .Include(e => e.Lines)
                    .FirstOrDefaultAsync(e => e.Id == id, ct);

                if (entry == null)
                    return new BaseResponse<JournalEntryDto>(false, "Journal entry not found", "", null);

                if (entry.IsPosted)
                    return new BaseResponse<JournalEntryDto>(false, "Cannot update posted journal entry", "", null);

                if (entry.IsVoided)
                    return new BaseResponse<JournalEntryDto>(false, "Cannot update voided journal entry", "", null);

                // Validate new lines
                var createDto = new JournalEntryCreateDto(
                    dto.EntryDate,
                    dto.Description,
                    entry.ReferenceType,
                    entry.ReferenceId,
                    dto.Lines
                );

                var validation = await ValidateJournalEntryAsync(createDto, ct);
                if (!validation.IsValid)
                    return new BaseResponse<JournalEntryDto>(false, string.Join(", ", validation.Errors), "", null);

                // Update entry
                entry.EntryDate = dto.EntryDate;
                entry.Description = dto.Description.Trim();
                entry.TotalAmount = validation.TotalDebits;
                entry.ModifiedAt = DateTime.UtcNow;
                entry.ModifiedBy = modifiedBy;

                // Delete old lines
                foreach (var line in entry.Lines.ToList())
                {
                    entry.Lines.Remove(line);
                }
                await _uow.SaveChangesAsync(ct);

                // Create new lines
                var lineNumber = 1;
                foreach (var lineDto in dto.Lines)
                {
                    var line = new JournalEntryLine
                    {
                        JournalEntryId = entry.Id,
                        AccountId = lineDto.AccountId,
                        DebitAmount = lineDto.DebitAmount,
                        CreditAmount = lineDto.CreditAmount,
                        Description = lineDto.Description?.Trim(),
                        LineNumber = lineNumber++,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _journalLineRepo.AddAsync(line, ct);
                }

                await _uow.SaveChangesAsync(ct);

                _logger.LogInformation("Journal entry updated: {EntryNumber}", entry.EntryNumber);

                return await GetJournalEntryByIdAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating journal entry {Id}", id);
                return new BaseResponse<JournalEntryDto>(false, "Error updating journal entry", "", null);
            }
        }

        public async Task<BaseResponse<JournalEntryDto>> PostJournalEntryAsync(
            int id,
            int? postedBy,
            CancellationToken ct = default)
        {
            try
            {
                var entry = await _journalRepo.Query(asNoTracking: false)
                    .Include(e => e.Lines)
                        .ThenInclude(l => l.Account)
                        .ThenInclude (c => c.AccountType)
                    .FirstOrDefaultAsync(e => e.Id == id, ct);

                if (entry == null)
                    return new BaseResponse<JournalEntryDto>(false, "Journal entry not found", "", null);

                if (entry.IsPosted)
                    return new BaseResponse<JournalEntryDto>(false, "Journal entry already posted", "", null);

                if (entry.IsVoided)
                    return new BaseResponse<JournalEntryDto>(false, "Cannot post voided journal entry", "", null);

                // Validate before posting
                var canPost = await CanPostJournalEntryAsync(id, ct);
                if (!canPost.Success || !canPost.Data)
                    return new BaseResponse<JournalEntryDto>(false, canPost.Message ?? "Cannot post journal entry", "", null);

                // Update account balances
                foreach (var line in entry.Lines)
                {
                    var account = line.Account;
                    if (account == null)
                        continue;

                    if (account.AccountType.NormalBalance == "Debit")
                        account.CurrentBalance += line.DebitAmount - line.CreditAmount;
                    else
                        account.CurrentBalance += line.CreditAmount - line.DebitAmount;
                }

                // Mark as posted
                entry.IsPosted = true;
                entry.PostedAt = DateTime.UtcNow;
                entry.PostedBy = postedBy;

                await _uow.SaveChangesAsync(ct);

                _logger.LogInformation("Journal entry posted: {EntryNumber}", entry.EntryNumber);

                return await GetJournalEntryByIdAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error posting journal entry {Id}", id);
                return new BaseResponse<JournalEntryDto>(false, "Error posting journal entry", "", null);
            }
        }

        public async Task<BaseResponse<JournalEntryDto>> VoidJournalEntryAsync(
            int id,
            string voidReason,
            int? voidedBy,
            CancellationToken ct = default)
        {
            try
            {
                var entry = await _journalRepo.Query(asNoTracking: false)
                    .Include(e => e.Lines)
                        .ThenInclude(l => l.Account)
                    .FirstOrDefaultAsync(e => e.Id == id, ct);

                if (entry == null)
                    return new BaseResponse<JournalEntryDto>(false, "Journal entry not found", "", null);

                if (!entry.IsPosted)
                    return new BaseResponse<JournalEntryDto>(false, "Cannot void unposted entry - delete it instead", "", null);

                if (entry.IsVoided)
                    return new BaseResponse<JournalEntryDto>(false, "Journal entry already voided", "", null);

                // Create reversing entry
                var reversingEntryDto = new JournalEntryCreateDto(
                    DateTime.UtcNow,
                    $"VOID: {entry.Description}",
                    "Adjustment",
                    null,
                    entry.Lines.Select(l => new JournalEntryLineCreateDto(
                        l.AccountId,
                        l.CreditAmount, // Swap debit and credit
                        l.DebitAmount,
                        $"Reversing: {l.Description}"
                    )).ToList()
                );

                var reversingResult = await CreateJournalEntryAsync(reversingEntryDto, voidedBy, ct);
                if (!reversingResult.Success)
                    return new BaseResponse<JournalEntryDto>(false, $"Error creating reversing entry: {reversingResult.Message}", "", null);

                // Post the reversing entry immediately
                await PostJournalEntryAsync(reversingResult.Data!.Id, voidedBy, ct);

                // Mark original as voided
                entry.IsVoided = true;
                entry.VoidedAt = DateTime.UtcNow;
                entry.VoidedBy = voidedBy;
                entry.VoidReason = voidReason;

                await _uow.SaveChangesAsync(ct);

                _logger.LogInformation("Journal entry voided: {EntryNumber}, Reversing entry: {ReversingNumber}",
                    entry.EntryNumber, reversingResult.Data.EntryNumber);

                return await GetJournalEntryByIdAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error voiding journal entry {Id}", id);
                return new BaseResponse<JournalEntryDto>(false, "Error voiding journal entry", "", null);
            }
        }

        public async Task<BaseResponse> DeleteJournalEntryAsync(int id, CancellationToken ct = default)
        {
            try
            {
                var entry = await _journalRepo.Query(asNoTracking: false)
                    .Include(e => e.Lines)
                    .FirstOrDefaultAsync(e => e.Id == id, ct);

                if (entry == null)
                    return new BaseResponse(false, "Journal entry not found");

                if (entry.IsPosted)
                    return new BaseResponse(false, "Cannot delete posted entry - void it instead");

                // Delete lines first
                foreach (var line in entry.Lines.ToList())
                {
                    entry.Lines.Remove(line);
                }

                await _uow.SaveChangesAsync(ct);

                // Delete entry
                _journalRepo.Remove(entry);
                await _uow.SaveChangesAsync(ct);

                _logger.LogInformation("Journal entry deleted: {EntryNumber}", entry.EntryNumber);

                return new BaseResponse(true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting journal entry {Id}", id);
                return new BaseResponse(false, "Error deleting journal entry");
            }
        }

        // ============================================
        // QUERY OPERATIONS
        // ============================================

        public async Task<BaseResponse<JournalEntryDto>> GetJournalEntryByIdAsync(int id, CancellationToken ct = default)
        {
            try
            {
                var entry = await _journalRepo.Query()
                    .Include(e => e.Lines)
                        .ThenInclude(l => l.Account)
                    .FirstOrDefaultAsync(e => e.Id == id, ct);

                if (entry == null)
                    return new BaseResponse<JournalEntryDto>(false, "Journal entry not found", "", null);

                var dto = _mapper.ToDto(entry);
                return new BaseResponse<JournalEntryDto>(true, null, "", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting journal entry {Id}", id);
                return new BaseResponse<JournalEntryDto>(false, "Error retrieving journal entry", "", null);
            }
        }

        public async Task<BaseResponse<JournalEntryDto>> GetJournalEntryByNumberAsync(string entryNumber, CancellationToken ct = default)
        {
            try
            {
                var entry = await _journalRepo.Query()
                    .Include(e => e.Lines)
                        .ThenInclude(l => l.Account)
                    .FirstOrDefaultAsync(e => e.EntryNumber == entryNumber, ct);

                if (entry == null)
                    return new BaseResponse<JournalEntryDto>(false, "Journal entry not found", "", null);

                var dto = _mapper.ToDto(entry);
                return new BaseResponse<JournalEntryDto>(true, null, "", dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting journal entry {EntryNumber}", entryNumber);
                return new BaseResponse<JournalEntryDto>(false, "Error retrieving journal entry", "", null);
            }
        }

        public async Task<BaseResponse<PagedResult<JournalEntryDto>>> SearchJournalEntriesAsync(
            JournalEntrySearchDto searchDto,
            CancellationToken ct = default)
        {
            try
            {
                var query = _journalRepo.Query()
                    .Include(e => e.Lines)
                        .ThenInclude(l => l.Account)
                    .AsQueryable();

                // Apply filters
                if (searchDto.FromDate.HasValue)
                    query = query.Where(e => e.EntryDate >= searchDto.FromDate.Value);

                if (searchDto.ToDate.HasValue)
                    query = query.Where(e => e.EntryDate <= searchDto.ToDate.Value);

                if (!string.IsNullOrWhiteSpace(searchDto.ReferenceType))
                    query = query.Where(e => e.ReferenceType == searchDto.ReferenceType);

                if (searchDto.ReferenceId.HasValue)
                    query = query.Where(e => e.ReferenceId == searchDto.ReferenceId.Value);

                if (searchDto.IsPosted.HasValue)
                    query = query.Where(e => e.IsPosted == searchDto.IsPosted.Value);

                if (searchDto.IsVoided.HasValue)
                    query = query.Where(e => e.IsVoided == searchDto.IsVoided.Value);

                // Get total count
                var totalCount = await query.CountAsync(ct);

                // Apply pagination
                var entries = await query
                    .OrderByDescending(e => e.EntryDate)
                    .ThenByDescending(e => e.EntryNumber)
                    .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
                    .Take(searchDto.PageSize)
                    .ToListAsync(ct);

                var dtos = entries.Select(e => _mapper.ToDto(e)).ToList();

                var totalPages = (int)Math.Ceiling(totalCount / (double)searchDto.PageSize);

                var result = new PagedResult<JournalEntryDto>(
                    dtos,
                    totalCount,
                    searchDto.PageNumber,
                    searchDto.PageSize,
                    totalPages
                );

                return new BaseResponse<PagedResult<JournalEntryDto>>(true, null, "", result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching journal entries");
                return new BaseResponse<PagedResult<JournalEntryDto>>(false, "Error searching journal entries", "", null);
            }
        }

        public async Task<BaseResponse<IReadOnlyList<JournalEntryDto>>> GetJournalEntriesByReferenceAsync(
            string referenceType,
            int referenceId,
            CancellationToken ct = default)
        {
            try
            {
                var entries = await _journalRepo.Query()
                    .Include(e => e.Lines)
                        .ThenInclude(l => l.Account)
                    .Where(e => e.ReferenceType == referenceType && e.ReferenceId == referenceId)
                    .OrderBy(e => e.EntryDate)
                    .ToListAsync(ct);

                var dtos = entries.Select(e => _mapper.ToDto(e)).ToList();

                return new BaseResponse<IReadOnlyList<JournalEntryDto>>(true, null, "", dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting journal entries for reference {Type}:{Id}", referenceType, referenceId);
                return new BaseResponse<IReadOnlyList<JournalEntryDto>>(false, "Error retrieving journal entries", "", null);
            }
        }

        // ============================================
        // AUTOMATED JOURNAL ENTRIES
        // ============================================

        public async Task<BaseResponse<JournalEntryDto>> CreateJournalEntryFromTransactionAsync(
            int transactionId,
            CancellationToken ct = default)
        {
            try
            {
                var transaction = await _transactionRepo.Query()
                    .Include(t => t.TransactionItems)
                        .ThenInclude(ti => ti.Item)
                            .ThenInclude(i => i.Category)
                    .Include(t => t.Game)
                        .ThenInclude(g => g.Category)
                    .FirstOrDefaultAsync(t => t.Id == transactionId, ct);

                if (transaction == null)
                    return new BaseResponse<JournalEntryDto>(false, "Transaction not found", "", null);

                if (transaction.StatusId != 6) // Not paid
                    return new BaseResponse<JournalEntryDto>(false, "Transaction is not paid yet", "", null);

                // Check if journal entry already exists
                var existingEntry = await _journalRepo.Query()
                    .FirstOrDefaultAsync(e => e.ReferenceType == "Transaction" && e.ReferenceId == transactionId, ct);

                if (existingEntry != null)
                    return new BaseResponse<JournalEntryDto>(false, "Journal entry already exists for this transaction", "", null);

                var lines = new List<JournalEntryLineCreateDto>();

                // DEBIT: Cash on Hand (1000)
                var cashAccount = await _accountRepo.Query()
                    .FirstOrDefaultAsync(a => a.AccountNumber == "1000", ct);

                if (cashAccount == null)
                    return new BaseResponse<JournalEntryDto>(false, "Cash account (1000) not found", "", null);

                lines.Add(new JournalEntryLineCreateDto(
                    cashAccount.Id,
                    transaction.TotalPrice,
                    0,
                    "Cash received"
                ));

                // CREDIT: Revenue accounts
                if (transaction.GameId != null)
                {
                    // Gaming revenue
                    var gameCategoryName = transaction.Game?.Category?.Name?.ToLower() ?? "";
                    string revenueAccountNumber = gameCategoryName switch
                    {
                        "ps5" => "4000",
                        "vr" => "4010",
                        "board games" => "4020",
                        _ => "4000" // Default to PS5
                    };

                    var revenueAccount = await _accountRepo.Query()
                        .FirstOrDefaultAsync(a => a.AccountNumber == revenueAccountNumber, ct);

                    if (revenueAccount != null)
                    {
                        lines.Add(new JournalEntryLineCreateDto(
                            revenueAccount.Id,
                            0,
                            transaction.TotalPrice,
                            $"Gaming revenue - {transaction.Game?.Name ?? "Game"}"
                        ));
                    }
                }
                else if (transaction.TransactionItems.Any())
                {
                    // FNB or TCG revenue
                    var itemsByCategory = transaction.TransactionItems
                        .GroupBy(ti => ti.Item?.Category?.Name?.ToLower() ?? "unknown");

                    foreach (var group in itemsByCategory)
                    {
                        var categoryName = group.Key;
                        var totalAmount = group.Sum(ti => ti.Item!.Price * ti.Quantity);

                        string revenueAccountNumber = categoryName switch
                        {
                            var cat when cat.Contains("food") || cat.Contains("snacks") || cat.Contains("breakfast")
                                => "4100", // FNB Revenue - Food
                            var cat when cat.Contains("beverages") || cat.Contains("drinks") || cat.Contains("coffee")
                                => "4110", // FNB Revenue - Beverages
                            var cat when cat.Contains("tcg") || cat.Contains("trading card")
                                => "4200", // TCG Retail Revenue
                            _ => "4100" // Default to food
                        };

                        var revenueAccount = await _accountRepo.Query()
                            .FirstOrDefaultAsync(a => a.AccountNumber == revenueAccountNumber, ct);

                        if (revenueAccount != null)
                        {
                            lines.Add(new JournalEntryLineCreateDto(
                                revenueAccount.Id,
                                0,
                                totalAmount,
                                $"Sales - {categoryName}"
                            ));
                        }
                    }
                }

                // Create journal entry
                var entryDto = new JournalEntryCreateDto(
                    transaction.CreatedOn,
                    $"Transaction #{transactionId} - Sale",
                    "Transaction",
                    transactionId,
                    lines
                );

                var result = await CreateJournalEntryAsync(entryDto, null, ct);

                if (result.Success)
                {
                    // Auto-post the entry
                    await PostJournalEntryAsync(result.Data!.Id, null, ct);
                    _logger.LogInformation("Auto-posted journal entry for transaction {TransactionId}", transactionId);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating journal entry from transaction {TransactionId}", transactionId);
                return new BaseResponse<JournalEntryDto>(false, "Error creating journal entry from transaction", "", null);
            }
        }

        public async Task<BaseResponse<JournalEntryOneDto>> CreateJournalEntryFromExpenseAsync(
            int expenseId,
            CancellationToken ct = default)
        {
            try
            {
                var expense = await _expenseRepo.Query()
                    .Include(e => e.Category)  // Include category
                    .ThenInclude(c => c.Account)  // Include mapped account
                    .FirstOrDefaultAsync(e => e.Id == expenseId, ct);

                if (expense == null)
                    return new BaseResponse<JournalEntryOneDto>(
                        false, "Expense not found", null);

                var cashAccount = await _accountRepo.Query()
                    .FirstOrDefaultAsync(a => a.AccountNumber == "1000" && a.IsActive, ct);

                if (cashAccount == null)
                    return new BaseResponse<JournalEntryOneDto>(
                        false, "Cash account (1000) not found", null);

                // Get expense account - USE MAPPING if available
                Account? expenseAccount = null;

                if (expense.Category.AccountId.HasValue)
                {
                    // Use mapped account
                    expenseAccount = await _accountRepo.Query()
                        .FirstOrDefaultAsync(
                            a => a.Id == expense.Category.AccountId.Value && a.IsActive,
                            ct);
                }

                // Fallback to keyword matching if no mapping
                if (expenseAccount == null)
                {
                    expenseAccount = await DetermineExpenseAccountAsync(
                        expense.Category.Name,
                        ct);
                }

                // Final fallback to miscellaneous
                if (expenseAccount == null)
                {
                    expenseAccount = await _accountRepo.Query()
                        .FirstOrDefaultAsync(
                            a => a.AccountNumber == "5900" && a.IsActive,
                            ct);
                }

                if (expenseAccount == null)
                    return new BaseResponse<JournalEntryOneDto>(
                        false, "No suitable expense account found", null);

                // Create journal entry...
                var entryNumber = await GenerateEntryNumberAsync(ct);
                var entry = new JournalEntry
                {
                    EntryNumber = entryNumber,
                    EntryDate = expense.CreatedOn,
                    Description = $"Expense #{expense.Id} - {expense.Category.Name}",
                    ReferenceType = "Expense",
                    ReferenceId = expense.Id,
                    TotalAmount = expense.Amount,
                    IsPosted = true,
                    IsVoided = false,
                    CreatedAt = DateTime.UtcNow
                };

                await _journalRepo.AddAsync(entry, ct);
                await _uow.SaveChangesAsync(ct);

                // DEBIT Expense
                var debitLine = new JournalEntryLine
                {
                    JournalEntryId = entry.Id,
                    AccountId = expenseAccount.Id,
                    DebitAmount = expense.Amount,
                    CreditAmount = 0,
                    Description = expense.Comment ?? expense.Category.Name,
                    LineNumber = 1,
                    CreatedAt = DateTime.UtcNow
                };

                // CREDIT Cash
                var creditLine = new JournalEntryLine
                {
                    JournalEntryId = entry.Id,
                    AccountId = cashAccount.Id,
                    DebitAmount = 0,
                    CreditAmount = expense.Amount,
                    Description = "Cash paid",
                    LineNumber = 2,
                    CreatedAt = DateTime.UtcNow
                };

                await _journalLineRepo.AddAsync(debitLine, ct);
                await _journalLineRepo.AddAsync(creditLine, ct);
                await _uow.SaveChangesAsync(ct);

                // Update balances
                expenseAccount.CurrentBalance += expense.Amount;
                cashAccount.CurrentBalance -= expense.Amount;
                _accountRepo.Update(expenseAccount);
                _accountRepo.Update(cashAccount);
                await _uow.SaveChangesAsync(ct);

                return new BaseResponse<JournalEntryOneDto>(
                    true, null, null,
                    await MapToDto(entry, ct));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error creating journal entry from expense {ExpenseId}",
                    expenseId);
                return new BaseResponse<JournalEntryOneDto>(
                    false, "Error creating journal entry", null);
            }
        }

        // Fallback keyword matching
        private async Task<Account?> DetermineExpenseAccountAsync(
            string categoryName,
            CancellationToken ct)
        {
            var lowerName = categoryName.ToLower();

            if (lowerName.Contains("rent"))
                return await GetAccountByNumberAsync("5100", ct);
            if (lowerName.Contains("utilit") || lowerName.Contains("electric"))
                return await GetAccountByNumberAsync("5200", ct);
            if (lowerName.Contains("internet") || lowerName.Contains("telecom"))
                return await GetAccountByNumberAsync("5300", ct);
            if (lowerName.Contains("salary") || lowerName.Contains("wage"))
                return await GetAccountByNumberAsync("5400", ct);
            if (lowerName.Contains("marketing") || lowerName.Contains("social"))
                return await GetAccountByNumberAsync("5500", ct);
            if (lowerName.Contains("maintenance") || lowerName.Contains("repair"))
                return await GetAccountByNumberAsync("5600", ct);
            if (lowerName.Contains("office") || lowerName.Contains("supplies"))
                return await GetAccountByNumberAsync("5800", ct);

            return null;
        }

        private async Task<Account?> GetAccountByNumberAsync(
            string accountNumber,
            CancellationToken ct)
        {
            return await _accountRepo.Query()
                .FirstOrDefaultAsync(
                    a => a.AccountNumber == accountNumber && a.IsActive,
                    ct);
        }


        // ============================================
        // VALIDATION
        // ============================================

        public async Task<JournalEntryValidationDto> ValidateJournalEntryAsync(
            JournalEntryCreateDto dto,
            CancellationToken ct = default)
        {
            var errors = new List<string>();

            // Validate basic fields
            if (string.IsNullOrWhiteSpace(dto.Description))
                errors.Add("Description is required");

            if (dto.Lines == null || dto.Lines.Count < 2)
                errors.Add("Journal entry must have at least 2 lines");

            if (dto.Lines != null)
            {
                // Validate each line
                foreach (var line in dto.Lines)
                {
                    if (line.DebitAmount == 0 && line.CreditAmount == 0)
                        errors.Add("Line must have either debit or credit amount");

                    if (line.DebitAmount < 0 || line.CreditAmount < 0)
                        errors.Add("Amounts cannot be negative");

                    if (line.DebitAmount > 0 && line.CreditAmount > 0)
                        errors.Add("Line cannot have both debit and credit");

                    // Check account exists
                    var accountExists = await _accountRepo.Query()
                        .AnyAsync(a => a.Id == line.AccountId && a.IsActive, ct);

                    if (!accountExists)
                        errors.Add($"Account {line.AccountId} not found or inactive");
                }

                // Calculate totals
                var totalDebits = dto.Lines.Sum(l => l.DebitAmount);
                var totalCredits = dto.Lines.Sum(l => l.CreditAmount);

                var isBalanced = Math.Abs(totalDebits - totalCredits) < 0.01m;

                if (!isBalanced)
                    errors.Add($"Debits ({totalDebits:C}) must equal Credits ({totalCredits:C})");

                return new JournalEntryValidationDto(
                    errors.Count == 0,
                    errors,
                    totalDebits,
                    totalCredits,
                    isBalanced
                );
            }

            return new JournalEntryValidationDto(
                false,
                errors,
                0,
                0,
                false
            );
        }

        public async Task<BaseResponse<bool>> CanPostJournalEntryAsync(int id, CancellationToken ct = default)
        {
            try
            {
                var entry = await _journalRepo.Query()
                    .Include(e => e.Lines)
                    .FirstOrDefaultAsync(e => e.Id == id, ct);

                if (entry == null)
                    return new BaseResponse<bool>(false, "Journal entry not found", "", false);

                if (entry.IsPosted)
                    return new BaseResponse<bool>(false, "Entry already posted", "", false);

                if (entry.IsVoided)
                    return new BaseResponse<bool>(false, "Entry is voided", "", false);

                if (entry.Lines.Count < 2)
                    return new BaseResponse<bool>(false, "Entry must have at least 2 lines", "", false);

                var totalDebits = entry.Lines.Sum(l => l.DebitAmount);
                var totalCredits = entry.Lines.Sum(l => l.CreditAmount);

                if (Math.Abs(totalDebits - totalCredits) >= 0.01m)
                    return new BaseResponse<bool>(false, "Debits must equal credits", "", false);

                return new BaseResponse<bool>(true, null, "", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if journal entry can be posted {Id}", id);
                return new BaseResponse<bool>(false, "Error validating journal entry", "", false);
            }
        }

        public async Task<BaseResponse<bool>> CanVoidJournalEntryAsync(int id, CancellationToken ct = default)
        {
            try
            {
                var entry = await _journalRepo.Query()
                    .FirstOrDefaultAsync(e => e.Id == id, ct);

                if (entry == null)
                    return new BaseResponse<bool>(false, "Journal entry not found", "", false);

                if (!entry.IsPosted)
                    return new BaseResponse<bool>(false, "Only posted entries can be voided", "", false);

                if (entry.IsVoided)
                    return new BaseResponse<bool>(false, "Entry already voided", "", false);

                return new BaseResponse<bool>(true, null, "", true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if journal entry can be voided {Id}", id);
                return new BaseResponse<bool>(false, "Error validating journal entry", "", false);
            }
        }

        // ============================================
        // UTILITIES
        // ============================================

        public async Task<string> GenerateEntryNumberAsync(CancellationToken ct = default)
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"JE-{year}-";

            // Get the last entry number for this year
            var lastEntry = await _journalRepo.Query()
                .Where(e => e.EntryNumber.StartsWith(prefix))
                .OrderByDescending(e => e.EntryNumber)
                .FirstOrDefaultAsync(ct);

            int sequence = 1;
            if (lastEntry != null)
            {
                var lastNumber = lastEntry.EntryNumber.Replace(prefix, "");
                if (int.TryParse(lastNumber, out int lastSeq))
                {
                    sequence = lastSeq + 1;
                }
            }

            return $"{prefix}{sequence:D5}"; // JE-2024-00001
        }

        public async Task<BaseResponse> RecalculateAccountBalancesAsync(CancellationToken ct = default)
        {
            try
            {
                _logger.LogInformation("Starting account balance recalculation...");

                var accounts = await _accountRepo.Query(asNoTracking: false)
                    .Include(a => a.AccountType)
                    .ToListAsync(ct);

                foreach (var account in accounts)
                {
                    var lines = await _journalLineRepo.Query()
                        .Where(l => l.AccountId == account.Id &&
                                   l.JournalEntry.IsPosted &&
                                   !l.JournalEntry.IsVoided)
                        .ToListAsync(ct);

                    var totalDebits = lines.Sum(l => l.DebitAmount);
                    var totalCredits = lines.Sum(l => l.CreditAmount);

                    account.CurrentBalance = account.AccountType.NormalBalance == "Debit"
                        ? totalDebits - totalCredits
                        : totalCredits - totalDebits;
                }

                await _uow.SaveChangesAsync(ct);

                _logger.LogInformation("Account balance recalculation completed for {Count} accounts", accounts.Count);

                return new BaseResponse(true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recalculating account balances");
                return new BaseResponse(false, "Error recalculating account balances");
            }
        }
        // Add this method to your JournalService class

        private async Task<JournalEntryOneDto> MapToDto(JournalEntry entry, CancellationToken ct)
        {
            // Load the full entry with related data
            var fullEntry = await _journalRepo.Query()
                .Include(e => e.Lines)
                    .ThenInclude(l => l.Account)
                .FirstOrDefaultAsync(e => e.Id == entry.Id, ct);

            if (fullEntry == null)
                throw new InvalidOperationException("Journal entry not found");

            var lines = fullEntry.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new JournalEntryLineOneDto(
                    l.Id,
                    l.AccountId,
                    l.Account.AccountNumber,
                    l.Account.AccountName,
                    l.DebitAmount,
                    l.CreditAmount,
                    l.Description,
                    l.LineNumber
                ))
                .ToList();

            return new JournalEntryOneDto(
                fullEntry.Id,
                fullEntry.EntryNumber,
                fullEntry.EntryDate,
                fullEntry.Description,
                fullEntry.ReferenceType,
                fullEntry.ReferenceId,
                fullEntry.TotalAmount,
                fullEntry.IsPosted,
                fullEntry.IsVoided,
                fullEntry.CreatedAt,
                lines
            );
        }

    }
    // Application/DTOs/JournalEntryDto.cs
    public record JournalEntryOneDto(
        int Id,
        string EntryNumber,
        DateTime EntryDate,
        string Description,
        string? ReferenceType,
        int? ReferenceId,
        decimal TotalAmount,
        bool IsPosted,
        bool IsVoided,
        DateTime CreatedAt,
        List<JournalEntryLineOneDto> Lines
    );

    public record JournalEntryLineOneDto(
        int Id,
        int AccountId,
        string AccountNumber,
        string AccountName,
        decimal DebitAmount,
        decimal CreditAmount,
        string? Description,
        int LineNumber
    );
}