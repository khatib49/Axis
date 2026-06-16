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
                                .ThenInclude(c => c.Account)
                    .Include(t => t.Game)
                        .ThenInclude(g => g.Category)
                            .ThenInclude(c => c.Account)
                    .Include(t => t.Discount) // NEW: needed to split JE into gross + discount
                    .FirstOrDefaultAsync(t => t.Id == transactionId, ct);

                if (transaction == null)
                    return new BaseResponse<JournalEntryDto>(false, "Transaction not found", "", null);

                if (transaction.StatusId != 6)
                    return new BaseResponse<JournalEntryDto>(false, "Transaction is not paid yet", "", null);

                // Check if journal entry already exists
                var existingEntry = await _journalRepo.Query()
                    .FirstOrDefaultAsync(e => e.ReferenceType == "Transaction" && e.ReferenceId == transactionId, ct);

                if (existingEntry != null)
                    return new BaseResponse<JournalEntryDto>(false, "Journal entry already exists for this transaction", "", null);

                // Skip zero-amount transactions (test sessions, etc.)
                if (transaction.TotalPrice <= 0)
                    return new BaseResponse<JournalEntryDto>(false, "Transaction has zero amount, skipping", "", null);

                // ── Compute gross + discount amount ─────────────────────────
                // TotalPrice is the NET (what the customer actually paid). The
                // Discount.Percentage was applied at the cashier:
                //     net = gross * (1 - pct/100)   =>   gross = net / (1 - pct/100)
                // The 3-line JE now shows revenue at GROSS, with the discount
                // booked separately to 4900 Sales Discounts so the owner can
                // see how much margin is being given away.
                var net = transaction.TotalPrice;
                decimal gross = net;
                decimal discountAmount = 0m;
                var pct = transaction.Discount?.Percentage ?? 0;
                if (pct > 0 && pct < 100)
                {
                    var factor = 1m - (pct / 100m);
                    gross = Math.Round(net / factor, 2);
                    discountAmount = Math.Round(gross - net, 2);
                }
                else if (pct >= 100)
                {
                    // Degenerate 100% discount: there's no gross we can recover
                    // (everything was given away). Fall back to net=0 semantics.
                    gross = net;
                    discountAmount = 0m;
                }

                var lines = new List<JournalEntryLineCreateDto>();

                // DEBIT: Cash on Hand (1000) — what the customer actually paid
                var cashAccount = await _accountRepo.Query()
                    .FirstOrDefaultAsync(a => a.AccountNumber == "1000" && a.IsActive, ct);

                if (cashAccount == null)
                    return new BaseResponse<JournalEntryDto>(false, "Cash account (1000) not found", "", null);

                lines.Add(new JournalEntryLineCreateDto(
                    cashAccount.Id,
                    net,
                    0,
                    "Cash received"
                ));

                // DEBIT: 4900 Sales Discounts (contra-revenue) for the discount
                // amount, only if there is a discount. If 4900 isn't configured,
                // log a warning and fall back to NET revenue accounting so the
                // sale isn't blocked.
                Account? salesDiscountAccount = null;
                if (discountAmount > 0)
                {
                    salesDiscountAccount = await _accountRepo.Query()
                        .FirstOrDefaultAsync(a => a.AccountNumber == "4900" && a.IsActive, ct);

                    if (salesDiscountAccount == null)
                    {
                        _logger.LogWarning(
                            "Sales Discounts account (4900) not found. Tx {TxId} will book NET revenue instead of gross.",
                            transactionId);
                        // No 4900 → treat as if there were no discount (legacy NET behavior).
                        gross = net;
                        discountAmount = 0m;
                    }
                    else
                    {
                        lines.Add(new JournalEntryLineCreateDto(
                            salesDiscountAccount.Id,
                            discountAmount,
                            0,
                            $"Discount given ({pct}%)"
                        ));
                    }
                }

                // CREDIT: Revenue accounts — at GROSS now, distributed across
                // categories the same way as before.
                if (transaction.GameId != null)
                {
                    // ── Gaming transaction ──────────────────────────────────────
                    var gameCategory = transaction.Game?.Category;
                    var revenueAccount = gameCategory?.Account;

                    if (revenueAccount == null)
                    {
                        revenueAccount = await _accountRepo.Query()
                            .FirstOrDefaultAsync(a => a.AccountNumber == "4000" && a.IsActive, ct);
                    }

                    if (revenueAccount == null)
                        return new BaseResponse<JournalEntryDto>(false,
                            $"No revenue account mapped for game category '{gameCategory?.Name ?? "unknown"}' and default account 4000 not found", "", null);

                    lines.Add(new JournalEntryLineCreateDto(
                        revenueAccount.Id,
                        0,
                        gross,
                        $"Gaming revenue - {transaction.Game?.Name ?? "Game"}"
                    ));
                }
                else if (transaction.TransactionItems.Any())
                {
                    // ── FNB / TCG ───────────────────────────────────────────
                    // Distribute GROSS proportionally across item categories
                    // (same proportions as before, just on gross instead of net).
                    var itemsByCat = transaction.TransactionItems
                        .Where(ti => ti.Item != null)
                        .GroupBy(ti => ti.Item!.Category)
                        .Select(g => new
                        {
                            Category = g.Key,
                            FullPrice = g.Sum(ti => ti.Item!.Price * ti.Quantity)
                        })
                        .ToList();

                    var fullTotal = itemsByCat.Sum(x => x.FullPrice);

                    if (fullTotal == 0 || itemsByCat.Count == 0)
                        return new BaseResponse<JournalEntryDto>(false,
                            $"Transaction {transactionId}: no valid items found", "", null);

                    decimal totalCredited = 0m;
                    var catLines = new List<JournalEntryLineCreateDto>();

                    foreach (var catGroup in itemsByCat)
                    {
                        var proportion = catGroup.FullPrice / fullTotal;
                        var lineAmount = Math.Round(gross * proportion, 2);

                        Account? revenueAccount = catGroup.Category?.Account;

                        if (revenueAccount == null)
                        {
                            revenueAccount = await _accountRepo.Query()
                                .FirstOrDefaultAsync(a => a.AccountNumber == "4100" && a.IsActive, ct);
                        }

                        if (revenueAccount == null)
                        {
                            _logger.LogWarning(
                                "No revenue account for category '{Cat}' in Tx {TxId}",
                                catGroup.Category?.Name ?? "null", transactionId);
                            continue;
                        }

                        catLines.Add(new JournalEntryLineCreateDto(
                            revenueAccount.Id,
                            0,
                            lineAmount,
                            $"Sales - {catGroup.Category?.Name ?? "Item"}"
                        ));

                        totalCredited += lineAmount;
                    }

                    if (totalCredited == 0)
                        return new BaseResponse<JournalEntryDto>(false,
                            $"Transaction {transactionId}: no revenue lines could be created", "", null);

                    // Fix rounding so credit total equals gross
                    var roundingDiff = gross - totalCredited;
                    if (Math.Abs(roundingDiff) > 0 && catLines.Count > 0)
                    {
                        var last = catLines[^1];
                        catLines[^1] = last with { CreditAmount = last.CreditAmount + roundingDiff };
                        totalCredited += roundingDiff;
                    }

                    lines.AddRange(catLines);
                }
                else
                {
                    // Transaction has no game and no items — skip
                    return new BaseResponse<JournalEntryDto>(false,
                        $"Transaction {transactionId} has no game and no items, skipping", "", null);
                }

                // Final balance check before creating. With the 3-line shape:
                //   Debits  = net (cash) + discountAmount (4900) = gross
                //   Credits = gross (4xxx revenue)
                // They should be exactly equal; any tiny rounding goes to cash.
                var totalDebits = lines.Sum(l => l.DebitAmount);
                var totalCredits = lines.Sum(l => l.CreditAmount);

                if (Math.Abs(totalDebits - totalCredits) > 0.01m)
                {
                    _logger.LogWarning(
                        "Transaction {TxId}: entry not balanced. Debits={Debits}, Credits={Credits}. Forcing balance.",
                        transactionId, totalDebits, totalCredits);

                    var cashLine = lines.FirstOrDefault(l => l.DebitAmount > 0 && l.AccountId == cashAccount.Id);
                    if (cashLine != null)
                    {
                        var balanced = cashLine with { DebitAmount = cashLine.DebitAmount + (totalCredits - totalDebits) };
                        lines.Remove(cashLine);
                        lines.Add(balanced);
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
                    .Include(e => e.Category)
                    .ThenInclude(c => c.Account)
                    .FirstOrDefaultAsync(e => e.Id == expenseId, ct);

                if (expense == null)
                    return new BaseResponse<JournalEntryOneDto>(
                        false, "Expense not found", null);

                // Fetch with tracking so we can modify CurrentBalance and let EF persist it
                // without colliding with any instances already tracked in this DbContext
                // (e.g. from a preceding DeleteJournalEntriesForExpenseAsync in the same request).
                var cashAccount = await _accountRepo.Query(asNoTracking: false)
                    .FirstOrDefaultAsync(a => a.AccountNumber == "1000" && a.IsActive, ct);

                if (cashAccount == null)
                    return new BaseResponse<JournalEntryOneDto>(
                        false, "Cash account (1000) not found", null);

                Account? expenseAccount = null;

                if (expense.Category.AccountId.HasValue)
                {
                    expenseAccount = await _accountRepo.Query(asNoTracking: false)
                        .FirstOrDefaultAsync(
                            a => a.Id == expense.Category.AccountId.Value && a.IsActive,
                            ct);
                }

                if (expenseAccount == null)
                {
                    expenseAccount = await DetermineExpenseAccountAsync(
                        expense.Category.Name,
                        ct);
                }

                if (expenseAccount == null)
                {
                    expenseAccount = await _accountRepo.Query(asNoTracking: false)
                        .FirstOrDefaultAsync(
                            a => a.AccountNumber == "5900" && a.IsActive,
                            ct);
                }

                if (expenseAccount == null)
                    return new BaseResponse<JournalEntryOneDto>(
                        false, "No suitable expense account found", null);

                var allocations = BuildMonthlyAllocations(
                    expense.Amount,
                    expense.FromDate,
                    expense.ToDate);

                var currentMonthStart = FirstOfMonth(DateTime.UtcNow);

                JournalEntry? lastEntry = null;

                foreach (var (monthStart, allocation) in allocations)
                {
                    var entryDate = DateTime.SpecifyKind(monthStart, DateTimeKind.Utc);
                    var isPosted = entryDate <= currentMonthStart;

                    var entryNumber = await GenerateEntryNumberAsync(ct);
                    var monthLabel = monthStart.ToString("MMM yyyy");
                    var description = string.IsNullOrWhiteSpace(expense.Comment)
                        ? $"{expense.Category.Name} - {monthLabel}"
                        : $"{expense.Comment} - {monthLabel}";

                    var entry = new JournalEntry
                    {
                        EntryNumber = entryNumber,
                        EntryDate = entryDate,
                        Description = description,
                        ReferenceType = "Expense",
                        ReferenceId = expense.Id,
                        TotalAmount = allocation,
                        IsPosted = isPosted,
                        IsVoided = false,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _journalRepo.AddAsync(entry, ct);
                    await _uow.SaveChangesAsync(ct);

                    var debitLine = new JournalEntryLine
                    {
                        JournalEntryId = entry.Id,
                        AccountId = expenseAccount.Id,
                        DebitAmount = allocation,
                        CreditAmount = 0,
                        Description = description,
                        LineNumber = 1,
                        CreatedAt = DateTime.UtcNow
                    };

                    var creditLine = new JournalEntryLine
                    {
                        JournalEntryId = entry.Id,
                        AccountId = cashAccount.Id,
                        DebitAmount = 0,
                        CreditAmount = allocation,
                        Description = "Cash paid",
                        LineNumber = 2,
                        CreatedAt = DateTime.UtcNow
                    };

                    await _journalLineRepo.AddAsync(debitLine, ct);
                    await _journalLineRepo.AddAsync(creditLine, ct);
                    await _uow.SaveChangesAsync(ct);

                    if (isPosted)
                    {
                        expenseAccount.CurrentBalance += allocation;
                        cashAccount.CurrentBalance -= allocation;
                    }

                    lastEntry = entry;
                }

                if (allocations.Count > 0)
                {
                    _accountRepo.Update(expenseAccount);
                    _accountRepo.Update(cashAccount);
                    await _uow.SaveChangesAsync(ct);
                }

                return new BaseResponse<JournalEntryOneDto>(
                    true, null, null,
                    lastEntry != null ? await MapToDto(lastEntry, ct) : null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error creating journal entries from expense {ExpenseId}: {Message}",
                    expenseId,
                    ex.Message);
                return new BaseResponse<JournalEntryOneDto>(
                    false,
                    $"Error creating journal entries: {ex.GetBaseException().Message}",
                    null);
            }
        }

        public async Task<BaseResponse> DeleteJournalEntriesForExpenseAsync(
            int expenseId,
            CancellationToken ct = default)
        {
            try
            {
                var entries = await _journalRepo.Query()
                    .Include(e => e.Lines)
                    .Where(e => e.ReferenceType == "Expense" && e.ReferenceId == expenseId)
                    .ToListAsync(ct);

                if (entries.Count == 0)
                    return new BaseResponse(true, null);

                var accountIdsToReload = entries
                    .SelectMany(e => e.Lines)
                    .Select(l => l.AccountId)
                    .Distinct()
                    .ToList();

                var accounts = await _accountRepo.Query(asNoTracking: false)
                    .Where(a => accountIdsToReload.Contains(a.Id))
                    .ToDictionaryAsync(a => a.Id, ct);

                foreach (var entry in entries)
                {
                    if (entry.IsPosted && !entry.IsVoided)
                    {
                        foreach (var line in entry.Lines)
                        {
                            if (accounts.TryGetValue(line.AccountId, out var acct))
                            {
                                acct.CurrentBalance -= line.DebitAmount;
                                acct.CurrentBalance += line.CreditAmount;
                            }
                        }
                    }

                    foreach (var line in entry.Lines.ToList())
                    {
                        entry.Lines.Remove(line);
                    }
                }

                await _uow.SaveChangesAsync(ct);

                foreach (var entry in entries)
                {
                    _journalRepo.Remove(entry);
                }

                foreach (var acct in accounts.Values)
                {
                    _accountRepo.Update(acct);
                }

                await _uow.SaveChangesAsync(ct);

                return new BaseResponse(true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error deleting journal entries for expense {ExpenseId}",
                    expenseId);
                return new BaseResponse(false, "Error deleting journal entries");
            }
        }

        private static DateTime FirstOfMonth(DateTime d)
        {
            return new DateTime(d.Year, d.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        private static List<(DateTime MonthStart, decimal Allocation)> BuildMonthlyAllocations(
            decimal amount,
            DateTime fromDate,
            DateTime toDate)
        {
            var fromMonth = FirstOfMonth(fromDate);
            var toMonth = FirstOfMonth(toDate);

            var n = ((toMonth.Year - fromMonth.Year) * 12) + (toMonth.Month - fromMonth.Month) + 1;
            if (n < 1) n = 1;

            var result = new List<(DateTime, decimal)>(n);

            if (n == 1)
            {
                result.Add((fromMonth, amount));
                return result;
            }

            var monthly = Math.Round(amount / n, 2, MidpointRounding.AwayFromZero);
            var allocatedSoFar = 0m;

            for (int i = 0; i < n; i++)
            {
                var monthStart = fromMonth.AddMonths(i);
                decimal allocation;

                if (i == n - 1)
                {
                    allocation = amount - allocatedSoFar;
                }
                else
                {
                    allocation = monthly;
                    allocatedSoFar += monthly;
                }

                result.Add((monthStart, allocation));
            }

            return result;
        }

        // Keyword fallback was disabled on purpose. It used to silently route any
        // category whose name contained "utilit"/"electric" to 5200, "rent" to
        // 5100, etc. — which are header accounts in the chart of accounts. That's
        // how 5200 (Utilities Expense, header) ended up with ~$9k of postings
        // even though no category was mapped to it. Now every expense MUST have
        // its Category mapped to a real leaf account; if it isn't, we fall back
        // only to 5900 (Miscellaneous Expenses), and the caller surfaces an
        // error if even that doesn't exist. This keeps header accounts clean and
        // forces explicit, auditable mappings.
        private Task<Account?> DetermineExpenseAccountAsync(
            string categoryName,
            CancellationToken ct)
        {
            return Task.FromResult<Account?>(null);
        }

        private async Task<Account?> GetAccountByNumberAsync(
            string accountNumber,
            CancellationToken ct)
        {
            return await _accountRepo.Query(asNoTracking: false)
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