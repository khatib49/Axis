using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class AccountService : IAccountService
    {
        private readonly IBaseRepository<Account> _accountRepo;
        private readonly IBaseRepository<AccountType> _accountTypeRepo;
        private readonly IBaseRepository<JournalEntryLine> _journalLineRepo;
        private readonly IBaseRepository<JournalEntry> _journalRepo;
        private readonly IUnitOfWork _uow;
        private readonly AccountingMapper _mapper;
        private readonly ILogger<AccountService> _logger;

        public AccountService(
            IBaseRepository<Account> accountRepo,
            IBaseRepository<AccountType> accountTypeRepo,
            IBaseRepository<JournalEntryLine> journalLineRepo,
            IBaseRepository<JournalEntry> journalRepo,
            IUnitOfWork uow,
            AccountingMapper mapper,
            ILogger<AccountService> logger)
        {
            _accountRepo = accountRepo;
            _accountTypeRepo = accountTypeRepo;
            _journalLineRepo = journalLineRepo;
            _journalRepo = journalRepo;
            _uow = uow;
            _mapper = mapper;
            _logger = logger;
        }

        // ============================================
        // ACCOUNT TYPE OPERATIONS
        // ============================================

        public async Task<BaseResponse<IReadOnlyList<AccountTypeDto>>> GetAllAccountTypesAsync(CancellationToken ct = default)
        {
            try
            {
                var types = await _accountTypeRepo.Query()
                    .Where(at => at.IsActive)
                    .OrderBy(at => at.DisplayOrder)
                    .ToListAsync(ct);

                var dtos = types.Select(t => _mapper.ToDto(t)).ToList();

                return new BaseResponse<IReadOnlyList<AccountTypeDto>>(true, null, null, dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting account types");
                return new BaseResponse<IReadOnlyList<AccountTypeDto>>(false, "Error retrieving account types", null);
            }
        }

        public async Task<BaseResponse<AccountTypeDto>> GetAccountTypeByIdAsync(int id, CancellationToken ct = default)
        {
            try
            {
                var type = await _accountTypeRepo.Query()
                    .FirstOrDefaultAsync(at => at.Id == id, ct);

                if (type == null)
                    return new BaseResponse<AccountTypeDto>(false, "Account type not found", null);

                var dto = _mapper.ToDto(type);
                return new BaseResponse<AccountTypeDto>(true, null, null, dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting account type {Id}", id);
                return new BaseResponse<AccountTypeDto>(false, "Error retrieving account type", null);
            }
        }

        // ============================================
        // ACCOUNT OPERATIONS
        // ============================================

        public async Task<BaseResponse<AccountDto>> CreateAccountAsync(
            AccountCreateDto dto,
            int? createdBy,
            CancellationToken ct = default)
        {
            try
            {
                // Validate
                var validation = await ValidateAccountAsync(
                    dto.AccountNumber,
                    dto.AccountName,
                    dto.AccountTypeId,
                    dto.ParentAccountId,
                    null,
                    ct);

                if (!validation.IsValid)
                    return new BaseResponse<AccountDto>(false, string.Join(", ", validation.Errors), null);

                // Create account
                var account = new Account
                {
                    AccountNumber = dto.AccountNumber.Trim(),
                    AccountName = dto.AccountName.Trim(),
                    AccountTypeId = dto.AccountTypeId,
                    ParentAccountId = dto.ParentAccountId,
                    Description = dto.Description?.Trim(),
                    AllowManualEntry = dto.AllowManualEntry,
                    CurrentBalance = 0,
                    IsActive = true,
                    IsSystemAccount = false,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = createdBy
                };

                await _accountRepo.AddAsync(account, ct);
                await _uow.SaveChangesAsync(ct);

                // Reload with navigation properties
                var created = await _accountRepo.Query()
                    .Include(a => a.AccountType)
                    .Include(a => a.ParentAccount)
                    .FirstAsync(a => a.Id == account.Id, ct);

                var resultDto = _mapper.ToDto(created);

                _logger.LogInformation("Account created: {AccountNumber} - {AccountName}",
                    account.AccountNumber, account.AccountName);

                return new BaseResponse<AccountDto>(true, null, "Account created successfully", resultDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating account");
                return new BaseResponse<AccountDto>(false, "Error creating account", null);
            }
        }

        public async Task<BaseResponse<AccountDto>> UpdateAccountAsync(
            int id,
            AccountUpdateDto dto,
            int? modifiedBy,
            CancellationToken ct = default)
        {
            try
            {
                var account = await _accountRepo.Query(asNoTracking: false)
                    .Include(a => a.AccountType)
                    .Include(a => a.ParentAccount)
                    .FirstOrDefaultAsync(a => a.Id == id, ct);

                if (account == null)
                    return new BaseResponse<AccountDto>(false, "Account not found", null);

                // Cannot modify system accounts' key properties
                if (account.IsSystemAccount && (
                    account.AccountName != dto.AccountName ||
                    account.AllowManualEntry != dto.AllowManualEntry))
                {
                    return new BaseResponse<AccountDto>(false, "Cannot modify system account properties", null);
                }

                // Update fields
                account.AccountName = dto.AccountName.Trim();
                account.Description = dto.Description?.Trim();
                account.IsActive = dto.IsActive;
                account.AllowManualEntry = dto.AllowManualEntry;
                account.ModifiedAt = DateTime.UtcNow;
                account.ModifiedBy = modifiedBy;

                await _uow.SaveChangesAsync(ct);

                var resultDto = _mapper.ToDto(account);

                _logger.LogInformation("Account updated: {Id} - {AccountName}", id, account.AccountName);

                return new BaseResponse<AccountDto>(true, null, "Account updated successfully", resultDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating account {Id}", id);
                return new BaseResponse<AccountDto>(false, "Error updating account", null);
            }
        }

        public async Task<BaseResponse> DeactivateAccountAsync(int id, int? modifiedBy, CancellationToken ct = default)
        {
            try
            {
                var account = await _accountRepo.Query(asNoTracking: false)
                    .FirstOrDefaultAsync(a => a.Id == id, ct);

                if (account == null)
                    return new BaseResponse(false, "Account not found");

                if (account.IsSystemAccount)
                    return new BaseResponse(false, "Cannot deactivate system account");

                // Check if account has balance
                if (account.CurrentBalance != 0)
                    return new BaseResponse(false, "Cannot deactivate account with non-zero balance");

                // Check if account has child accounts
                var hasChildren = await _accountRepo.Query()
                    .AnyAsync(a => a.ParentAccountId == id && a.IsActive, ct);

                if (hasChildren)
                    return new BaseResponse(false, "Cannot deactivate account with active child accounts");

                account.IsActive = false;
                account.ModifiedAt = DateTime.UtcNow;
                account.ModifiedBy = modifiedBy;

                await _uow.SaveChangesAsync(ct);

                _logger.LogInformation("Account deactivated: {Id}", id);

                return new BaseResponse(true,"Account deactivated successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deactivating account {Id}", id);
                return new BaseResponse(false, "Error deactivating account");
            }
        }

        public async Task<BaseResponse<AccountDto>> GetAccountByIdAsync(int id, CancellationToken ct = default)
        {
            try
            {
                var account = await _accountRepo.Query()
                    .Include(a => a.AccountType)
                    .Include(a => a.ParentAccount)
                    .FirstOrDefaultAsync(a => a.Id == id, ct);

                if (account == null)
                    return new BaseResponse<AccountDto>(false, "Account not found", null);

                var dto = _mapper.ToDto(account);
                return new BaseResponse<AccountDto>(true, null, null, dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting account {Id}", id);
                return new BaseResponse<AccountDto>(false, "Error retrieving account", null);
            }
        }

        public async Task<BaseResponse<AccountDto>> GetAccountByNumberAsync(string accountNumber, CancellationToken ct = default)
        {
            try
            {
                var account = await _accountRepo.Query()
                    .Include(a => a.AccountType)
                    .Include(a => a.ParentAccount)
                    .FirstOrDefaultAsync(a => a.AccountNumber == accountNumber, ct);

                if (account == null)
                    return new BaseResponse<AccountDto>(false, "Account not found", null);

                var dto = _mapper.ToDto(account);
                return new BaseResponse<AccountDto>(true, null, null, dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting account {AccountNumber}", accountNumber);
                return new BaseResponse<AccountDto>(false, "Error retrieving account", null);
            }
        }

        public async Task<BaseResponse<IReadOnlyList<AccountDto>>> GetAllAccountsAsync(
            int? accountTypeId = null,
            bool? isActive = null,
            CancellationToken ct = default)
        {
            try
            {
                var query = _accountRepo.Query()
                    .Include(a => a.AccountType)
                    .Include(a => a.ParentAccount)
                    .AsQueryable();

                if (accountTypeId.HasValue)
                    query = query.Where(a => a.AccountTypeId == accountTypeId.Value);

                if (isActive.HasValue)
                    query = query.Where(a => a.IsActive == isActive.Value);

                var accounts = await query
                    .OrderBy(a => a.AccountNumber)
                    .ToListAsync(ct);

                var dtos = accounts.Select(a => _mapper.ToDto(a)).ToList();

                return new BaseResponse<IReadOnlyList<AccountDto>>(true, null, null, dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting accounts");
                return new BaseResponse<IReadOnlyList<AccountDto>>(false, "Error retrieving accounts", null);
            }
        }

        public async Task<BaseResponse<PagedResult<AccountDto>>> SearchAccountsAsync(
            AccountSearchDto searchDto,
            CancellationToken ct = default)
        {
            try
            {
                var query = _accountRepo.Query()
                    .Include(a => a.AccountType)
                    .Include(a => a.ParentAccount)
                    .AsQueryable();

                // Apply filters
                if (!string.IsNullOrWhiteSpace(searchDto.SearchTerm))
                {
                    var term = searchDto.SearchTerm.ToLower();
                    query = query.Where(a =>
                        a.AccountNumber.ToLower().Contains(term) ||
                        a.AccountName.ToLower().Contains(term));
                }

                if (searchDto.AccountTypeId.HasValue)
                    query = query.Where(a => a.AccountTypeId == searchDto.AccountTypeId.Value);

                if (searchDto.ParentAccountId.HasValue)
                    query = query.Where(a => a.ParentAccountId == searchDto.ParentAccountId.Value);

                if (searchDto.IsActive.HasValue)
                    query = query.Where(a => a.IsActive == searchDto.IsActive.Value);

                // Get total count
                var totalCount = await query.CountAsync(ct);

                // Apply pagination
                var accounts = await query
                    .OrderBy(a => a.AccountNumber)
                    .Skip((searchDto.PageNumber - 1) * searchDto.PageSize)
                    .Take(searchDto.PageSize)
                    .ToListAsync(ct);

                var dtos = accounts.Select(a => _mapper.ToDto(a)).ToList();

                var totalPages = (int)Math.Ceiling(totalCount / (double)searchDto.PageSize);

                var result = new PagedResult<AccountDto>(
                    dtos,
                    totalCount,
                    searchDto.PageNumber,
                    searchDto.PageSize,
                    totalPages
                );

                return new BaseResponse<PagedResult<AccountDto>>(true, null, null, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching accounts");
                return new BaseResponse<PagedResult<AccountDto>>(false, "Error searching accounts", null);
            }
        }

        public async Task<BaseResponse<IReadOnlyList<AccountHierarchyDto>>> GetAccountHierarchyAsync(
            int? accountTypeId = null,
            CancellationToken ct = default)
        {
            try
            {
                var query = _accountRepo.Query()
                    .Include(a => a.AccountType)
                    .Include(a => a.ChildAccounts)
                    .Where(a => a.IsActive && a.ParentAccountId == null);

                if (accountTypeId.HasValue)
                    query = query.Where(a => a.AccountTypeId == accountTypeId.Value);

                var rootAccounts = await query
                    .OrderBy(a => a.AccountNumber)
                    .ToListAsync(ct);

                // Load all child accounts recursively
                await LoadChildAccountsAsync(rootAccounts, ct);

                var dtos = rootAccounts.Select(a => _mapper.ToHierarchyDto(a)).ToList();

                return new BaseResponse<IReadOnlyList<AccountHierarchyDto>>(true, null, null, dtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting account hierarchy");
                return new BaseResponse<IReadOnlyList<AccountHierarchyDto>>(false, "Error retrieving account hierarchy", null);
            }
        }

        private async Task LoadChildAccountsAsync(IEnumerable<Account> accounts, CancellationToken ct)
        {
            foreach (var account in accounts)
            {
                if (account.ChildAccounts.Any())
                {
                    await _accountRepo.Query()
                        .Where(a => a.ParentAccountId == account.Id && a.IsActive)
                        .Include(a => a.AccountType)
                        .Include(a => a.ChildAccounts)
                        .LoadAsync(ct);

                    await LoadChildAccountsAsync(account.ChildAccounts, ct);
                }
            }
        }

        // ============================================
        // BALANCE & REPORTING
        // ============================================

        public async Task<BaseResponse<AccountBalanceDto>> GetAccountBalanceAsync(int accountId, CancellationToken ct = default)
        {
            try
            {
                var account = await _accountRepo.Query()
                    .Include(a => a.AccountType)
                    .FirstOrDefaultAsync(a => a.Id == accountId, ct);

                if (account == null)
                    return new BaseResponse<AccountBalanceDto>(false, "Account not found", null);

                // Calculate totals from journal entry lines
                var lines = await _journalLineRepo.Query()
                    .Where(l => l.AccountId == accountId && l.JournalEntry.IsPosted && !l.JournalEntry.IsVoided)
                    .ToListAsync(ct);

                var debitTotal = lines.Sum(l => l.DebitAmount);
                var creditTotal = lines.Sum(l => l.CreditAmount);

                var dto = _mapper.ToBalanceDto(account, debitTotal, creditTotal);

                return new BaseResponse<AccountBalanceDto>(true, null, null, dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting account balance {AccountId}", accountId);
                return new BaseResponse<AccountBalanceDto>(false, "Error retrieving account balance", null);
            }
        }

        public async Task<BaseResponse<IReadOnlyList<AccountBalanceDto>>> GetAllAccountBalancesAsync(CancellationToken ct = default)
        {
            try
            {
                var accounts = await _accountRepo.Query()
                    .Include(a => a.AccountType)
                    .Where(a => a.IsActive)
                    .OrderBy(a => a.AccountNumber)
                    .ToListAsync(ct);

                var balances = new List<AccountBalanceDto>();

                foreach (var account in accounts)
                {
                    var lines = await _journalLineRepo.Query()
                        .Where(l => l.AccountId == account.Id && l.JournalEntry.IsPosted && !l.JournalEntry.IsVoided)
                        .ToListAsync(ct);

                    var debitTotal = lines.Sum(l => l.DebitAmount);
                    var creditTotal = lines.Sum(l => l.CreditAmount);

                    balances.Add(_mapper.ToBalanceDto(account, debitTotal, creditTotal));
                }

                return new BaseResponse<IReadOnlyList<AccountBalanceDto>>(true, null, null, balances);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all account balances");
                return new BaseResponse<IReadOnlyList<AccountBalanceDto>>(false, "Error retrieving account balances", null);
            }
        }

        public async Task<BaseResponse<IReadOnlyList<AccountSummaryDto>>> GetAccountSummaryAsync(CancellationToken ct = default)
        {
            try
            {
                var balancesResponse = await GetAllAccountBalancesAsync(ct);
                if (!balancesResponse.Success)
                    return new BaseResponse<IReadOnlyList<AccountSummaryDto>>(false, balancesResponse.Message, null);

                var balances = balancesResponse.Data!;

                var accounts = await _accountRepo.Query()
                    .Include(a => a.AccountType)
                    .Where(a => a.IsActive)
                    .ToListAsync(ct);

                var grouped = accounts.GroupBy(a => a.AccountType.TypeName);

                var summary = grouped.Select(g => new AccountSummaryDto(
                    g.Key,
                    balances.Where(b => b.AccountTypeName == g.Key).Sum(b => b.Balance),
                    g.Select(a => _mapper.ToDto(a)).ToList()
                )).ToList();

                return new BaseResponse<IReadOnlyList<AccountSummaryDto>>(true, null, null, summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting account summary");
                return new BaseResponse<IReadOnlyList<AccountSummaryDto>>(false, "Error generating account summary", null);
            }
        }

        public async Task<BaseResponse<TrialBalanceDto>> GetTrialBalanceAsync(DateTime? asOfDate = null, CancellationToken ct = default)
        {
            try
            {
                // ✅ FIX: Ensure date is UTC
                var cutoffDate = asOfDate.HasValue
                    ? DateTime.SpecifyKind(asOfDate.Value, DateTimeKind.Utc)
                    : DateTime.UtcNow;

                var accounts = await _accountRepo.Query()
                    .Include(a => a.AccountType)
                    .Where(a => a.IsActive)
                    .OrderBy(a => a.AccountNumber)
                    .ToListAsync(ct);

                var lines = new List<TrialBalanceLineDto>();
                decimal totalDebits = 0;
                decimal totalCredits = 0;

                foreach (var account in accounts)
                {
                    var journalLines = await _journalLineRepo.Query()
                        .Where(l => l.AccountId == account.Id &&
                                   l.JournalEntry.IsPosted &&
                                   !l.JournalEntry.IsVoided &&
                                   l.JournalEntry.EntryDate <= cutoffDate)
                        .ToListAsync(ct);

                    var debitSum = journalLines.Sum(l => l.DebitAmount);
                    var creditSum = journalLines.Sum(l => l.CreditAmount);

                    var balance = account.AccountType.NormalBalance == "Debit"
                        ? debitSum - creditSum
                        : creditSum - debitSum;

                    if (balance != 0) // Only show accounts with balances
                    {
                        var isDebitBalance = (account.AccountType.NormalBalance == "Debit" && balance > 0) ||
                                           (account.AccountType.NormalBalance == "Credit" && balance < 0);

                        var debitBalance = isDebitBalance ? Math.Abs(balance) : 0;
                        var creditBalance = !isDebitBalance ? Math.Abs(balance) : 0;

                        lines.Add(_mapper.ToTrialBalanceLineDto(
                            account.AccountNumber,
                            account.AccountName,
                            account.AccountType.TypeName,
                            debitBalance,
                            creditBalance
                        ));

                        totalDebits += debitBalance;
                        totalCredits += creditBalance;
                    }
                }

                var isBalanced = Math.Abs(totalDebits - totalCredits) < 0.01m;

                var result = new TrialBalanceDto(
                    cutoffDate,
                    lines,
                    totalDebits,
                    totalCredits,
                    isBalanced
                );

                return new BaseResponse<TrialBalanceDto>(true, null, null, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating trial balance");
                return new BaseResponse<TrialBalanceDto>(false, "Error generating trial balance", null);
            }
        }

        public async Task<BaseResponse<GeneralLedgerDto>> GetGeneralLedgerAsync(
    int accountId,
    DateTime? fromDate = null,
    DateTime? toDate = null,
    CancellationToken ct = default)
        {
            try
            {
                // Set longer timeout for this query
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

                // Lazy-post any expense-derived journal entries whose EntryDate has now arrived.
                // Picks up monthly amortization rows that were pre-created with IsPosted=false
                // and rolls them forward without needing a scheduled job.
                await AutoPostDueExpenseEntriesAsync(linkedCts.Token);

                var account = await _accountRepo.Query()
                    .Include(a => a.AccountType)
                    .FirstOrDefaultAsync(a => a.Id == accountId, linkedCts.Token);

                if (account == null)
                    return new BaseResponse<GeneralLedgerDto>(false, "Account not found", null);

                // ✅ FIX: Ensure all dates are UTC
                var startDate = fromDate.HasValue
                    ? DateTime.SpecifyKind(fromDate.Value, DateTimeKind.Utc)
                    : DateTime.UtcNow.AddYears(-1);

                var endDate = toDate.HasValue
                    ? DateTime.SpecifyKind(toDate.Value, DateTimeKind.Utc)
                    : DateTime.UtcNow;

                // OPTIMIZED: Single query with conditional aggregation
                var allLines = await _journalLineRepo.Query()
                    .Include(l => l.JournalEntry)
                    .Where(l => l.AccountId == accountId &&
                               l.JournalEntry.IsPosted &&
                               !l.JournalEntry.IsVoided &&
                               l.JournalEntry.EntryDate < endDate)
                    .OrderBy(l => l.JournalEntry.EntryDate)
                    .ThenBy(l => l.JournalEntry.EntryNumber)
                    .ToListAsync(linkedCts.Token);

                // Calculate opening balance from in-memory data
                var openingLines = allLines.Where(l => l.JournalEntry.EntryDate < startDate).ToList();
                var openingDebits = openingLines.Sum(l => l.DebitAmount);
                var openingCredits = openingLines.Sum(l => l.CreditAmount);
                var openingBalance = account.AccountType.NormalBalance == "Debit"
                    ? openingDebits - openingCredits
                    : openingCredits - openingDebits;

                // Get period transactions from in-memory data
                var periodLines = allLines.Where(l => l.JournalEntry.EntryDate >= startDate).ToList();

                var runningBalance = openingBalance;
                var transactions = new List<GeneralLedgerLineDto>();

                foreach (var line in periodLines)
                {
                    if (account.AccountType.NormalBalance == "Debit")
                        runningBalance += line.DebitAmount - line.CreditAmount;
                    else
                        runningBalance += line.CreditAmount - line.DebitAmount;

                    transactions.Add(_mapper.ToGeneralLedgerLineDto(
                        line.JournalEntry.EntryDate,
                        line.JournalEntry.EntryNumber,
                        line.Description ?? line.JournalEntry.Description,
                        line.DebitAmount,
                        line.CreditAmount,
                        runningBalance
                    ));
                }

                // Append pending (unposted, future-dated) expense lines as informational credits.
                // These represent amounts not yet recognized; running balance does not change.
                var pendingLines = await _journalLineRepo.Query()
                    .Include(l => l.JournalEntry)
                    .Where(l => l.AccountId == accountId &&
                               !l.JournalEntry.IsPosted &&
                               !l.JournalEntry.IsVoided &&
                               l.JournalEntry.ReferenceType == "Expense" &&
                               l.JournalEntry.EntryDate >= startDate &&
                               l.JournalEntry.EntryDate < endDate &&
                               l.DebitAmount > 0)
                    .OrderBy(l => l.JournalEntry.EntryDate)
                    .ThenBy(l => l.JournalEntry.EntryNumber)
                    .ToListAsync(linkedCts.Token);

                foreach (var line in pendingLines)
                {
                    transactions.Add(new GeneralLedgerLineDto(
                        line.JournalEntry.EntryDate,
                        line.JournalEntry.EntryNumber,
                        line.Description ?? line.JournalEntry.Description,
                        0m,
                        line.DebitAmount,
                        runningBalance,
                        true
                    ));
                }

                var result = new GeneralLedgerDto(
                    accountId,
                    account.AccountNumber,
                    account.AccountName,
                    startDate,
                    endDate,
                    openingBalance,
                    transactions,
                    runningBalance
                );

                return new BaseResponse<GeneralLedgerDto>(true, null, null, result);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("General ledger query was canceled for account {AccountId}", accountId);
                return new BaseResponse<GeneralLedgerDto>(false, "Request was canceled or timed out", null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating general ledger for account {AccountId}", accountId);
                return new BaseResponse<GeneralLedgerDto>(false, "Error generating general ledger", null);
            }
        }

        // Promote any unposted expense JEs whose EntryDate has now arrived. Called from
        // GetGeneralLedgerAsync so monthly amortization rolls forward without a cron job.
        private async Task AutoPostDueExpenseEntriesAsync(CancellationToken ct)
        {
            try
            {
                var today = DateTime.UtcNow.Date;

                var dueEntries = await _journalRepo.Query(asNoTracking: false)
                    .Include(e => e.Lines)
                    .Where(e => !e.IsPosted &&
                                !e.IsVoided &&
                                e.ReferenceType == "Expense" &&
                                e.EntryDate <= today)
                    .ToListAsync(ct);

                if (dueEntries.Count == 0)
                    return;

                var accountIds = dueEntries
                    .SelectMany(e => e.Lines)
                    .Select(l => l.AccountId)
                    .Distinct()
                    .ToList();

                var accounts = await _accountRepo.Query(asNoTracking: false)
                    .Where(a => accountIds.Contains(a.Id))
                    .ToDictionaryAsync(a => a.Id, ct);

                foreach (var entry in dueEntries)
                {
                    foreach (var line in entry.Lines)
                    {
                        if (accounts.TryGetValue(line.AccountId, out var acct))
                        {
                            acct.CurrentBalance += line.DebitAmount;
                            acct.CurrentBalance -= line.CreditAmount;
                        }
                    }

                    entry.IsPosted = true;
                    entry.PostedAt = DateTime.UtcNow;
                }

                foreach (var acct in accounts.Values)
                {
                    _accountRepo.Update(acct);
                }

                await _uow.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Auto-posted {Count} due expense journal entries",
                    dueEntries.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error auto-posting due expense journal entries");
            }
        }


        // ============================================
        // VALIDATION
        // ============================================

        public async Task<AccountValidationDto> ValidateAccountAsync(
            string accountNumber,
            string accountName,
            int accountTypeId,
            int? parentAccountId,
            int? existingAccountId = null,
            CancellationToken ct = default)
        {
            var errors = new List<string>();

            // Validate account number
            if (string.IsNullOrWhiteSpace(accountNumber))
                errors.Add("Account number is required");
            else
            {
                var numberExists = await _accountRepo.Query()
                    .AnyAsync(a => a.AccountNumber == accountNumber &&
                                  (!existingAccountId.HasValue || a.Id != existingAccountId.Value), ct);
                if (numberExists)
                    errors.Add($"Account number '{accountNumber}' already exists");
            }

            // Validate account name
            if (string.IsNullOrWhiteSpace(accountName))
                errors.Add("Account name is required");
            else if (accountName.Length < 3)
                errors.Add("Account name must be at least 3 characters");

            // Validate account type
            var typeExists = await _accountTypeRepo.Query()
                .AnyAsync(at => at.Id == accountTypeId, ct);
            if (!typeExists)
                errors.Add("Invalid account type");

            // Validate parent account
            var parentValid = true;
            if (parentAccountId.HasValue)
            {
                var parent = await _accountRepo.Query()
                    .FirstOrDefaultAsync(a => a.Id == parentAccountId.Value, ct);

                if (parent == null)
                {
                    errors.Add("Parent account not found");
                    parentValid = false;
                }
                else if (parent.AccountTypeId != accountTypeId)
                {
                    errors.Add("Parent account must be of the same type");
                    parentValid = false;
                }
            }

            return new AccountValidationDto(
                errors.Count == 0,
                errors,
                !errors.Any(e => e.Contains("number")),
                !errors.Any(e => e.Contains("name")),
                parentValid
            );
        }

        public async Task<List<ExpenseAccountDto>> GetExpenseAccountsAsync(CancellationToken ct)
        {
            var accounts = await _accountRepo.Query()
                .Where(a => a.AccountNumber.StartsWith("5") && a.IsActive)
                .OrderBy(a => a.AccountNumber)
                .Select(a => new ExpenseAccountDto
                {
                    Id = a.Id,
                    AccountNumber = a.AccountNumber,
                    AccountName = a.AccountName
                })
                .ToListAsync(ct);

            return accounts;
        }

        // Returns every active account that can have manual postings, regardless
        // of type. The Expense-Categories UI uses this so a category can be mapped
        // to an Equity account (e.g. Owner Draws / "Omar cash out") or a Revenue
        // account (e.g. "Toters income") — not just 5xxx expense accounts.
        public async Task<List<PostableAccountDto>> GetPostableAccountsAsync(CancellationToken ct)
        {
            return await _accountRepo.Query()
                .Where(a => a.IsActive && a.AllowManualEntry)
                .OrderBy(a => a.AccountNumber)
                .Select(a => new PostableAccountDto
                {
                    Id = a.Id,
                    AccountNumber = a.AccountNumber,
                    AccountName = a.AccountName,
                    AccountTypeId = a.AccountTypeId,
                    AccountTypeName = a.AccountType.TypeName
                })
                .ToListAsync(ct);
        }

    }

    public class ExpenseAccountDto
    {
        public int Id { get; set; }
        public string AccountNumber { get; set; }
        public string AccountName { get; set; }
    }

    public class PostableAccountDto
    {
        public int Id { get; set; }
        public string AccountNumber { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public int AccountTypeId { get; set; }
        public string AccountTypeName { get; set; } = string.Empty;
    }
}