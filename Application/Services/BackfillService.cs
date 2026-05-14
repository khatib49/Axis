using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Hangfire;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class BackfillService : IBackfillService
    {
        private readonly IBaseRepository<TransactionRecord> _txRepo;
        private readonly IBaseRepository<Expense> _expenseRepo;
        private readonly IBaseRepository<JournalEntry> _journalRepo;
        private readonly IBaseRepository<JournalEntryLine> _journalLineRepo;
        private readonly IBaseRepository<Account> _accountRepo;
        private readonly IBaseRepository<Category> _categoryRepo;
        private readonly IBaseRepository<ExpenseCategory> _expenseCategoryRepo;
        private readonly IJournalService _journalService;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<BackfillService> _logger;

        public BackfillService(
            IBaseRepository<TransactionRecord> txRepo,
            IBaseRepository<Expense> expenseRepo,
            IBaseRepository<JournalEntry> journalRepo,
            IBaseRepository<JournalEntryLine> journalLineRepo,
            IBaseRepository<Account> accountRepo,
            IBaseRepository<Category> categoryRepo,
            IBaseRepository<ExpenseCategory> expenseCategoryRepo,
            IJournalService journalService,
            IUnitOfWork uow,
            ILogger<BackfillService> logger)
        {
            _txRepo = txRepo;
            _expenseRepo = expenseRepo;
            _journalRepo = journalRepo;
            _journalLineRepo = journalLineRepo;
            _accountRepo = accountRepo;
            _categoryRepo = categoryRepo;
            _expenseCategoryRepo = expenseCategoryRepo;
            _journalService = journalService;
            _uow = uow;
            _logger = logger;
        }

        // ============================================
        // BACKGROUND ENTRY POINTS (Hangfire)
        // ============================================

        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        [AutomaticRetry(Attempts = 0)]
        public Task BackfillTransactionsBgAsync(CancellationToken ct)
            => BackfillTransactionsAsync(ct);

        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        [AutomaticRetry(Attempts = 0)]
        public Task BackfillExpensesBgAsync(CancellationToken ct)
            => BackfillExpensesAsync(ct);

        // ============================================
        // TRANSACTIONS — fast bulk + in-place re-route
        // ============================================

        public async Task<BackfillResultDto> BackfillTransactionsAsync(CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // 1) Pull every completed, non-zero transaction in one shot, with everything
            // needed to compute the right revenue account per line.
            var transactions = await _txRepo.Query()
                .Include(t => t.TransactionItems)
                    .ThenInclude(ti => ti.Item)
                        .ThenInclude(i => i.Category)
                .Include(t => t.Game)
                    .ThenInclude(g => g.Category)
                .Where(t => t.StatusId == 6 && t.TotalPrice > 0)
                .AsNoTracking()
                .ToListAsync(ct);

            if (transactions.Count == 0)
                return new BackfillResultDto(0, 0, 0, new List<string>());

            // 2) Load all accounts (tracked, so balance updates are persisted by SaveChanges).
            var accounts = await _accountRepo.Query(asNoTracking: false)
                .Include(a => a.AccountType)
                .ToDictionaryAsync(a => a.Id, ct);

            var accountsByNumber = accounts.Values
                .Where(a => a.IsActive)
                .ToDictionary(a => a.AccountNumber, a => a);

            if (!accountsByNumber.TryGetValue("1000", out var cashAccount))
                return new BackfillResultDto(0, 0, transactions.Count,
                    new List<string> { "Cash account (1000) not found" });

            accountsByNumber.TryGetValue("4000", out var fallbackGamingRevenue);
            accountsByNumber.TryGetValue("4100", out var fallbackItemRevenue);

            // 3) Pull all existing transaction JEs once, keyed by ReferenceId.
            var txIds = transactions.Select(t => t.Id).ToList();
            var existingEntries = await _journalRepo.Query(asNoTracking: false)
                .Include(je => je.Lines)
                .Where(je => je.ReferenceType == "Transaction"
                          && je.ReferenceId != null
                          && txIds.Contains(je.ReferenceId.Value))
                .ToListAsync(ct);
            var existingByTx = existingEntries
                .GroupBy(je => je.ReferenceId!.Value)
                .ToDictionary(g => g.Key, g => g.First());

            // 4) Build category lookup by name (case-insensitive) so we can re-route
            // existing lines back to their category's currently-mapped account using the
            // line description ("Sales - {CategoryName}") as the key.
            var allCategories = await _categoryRepo.Query()
                .AsNoTracking()
                .ToListAsync(ct);
            var categoriesByName = allCategories
                .GroupBy(c => (c.Name ?? string.Empty).Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            // 5) Pre-allocate journal entry numbers locally so we don't round-trip the DB
            // once per insert.
            var nextSeq = await ComputeNextEntryNumberSequenceAsync(ct);

            int created = 0, repointed = 0, skipped = 0, failed = 0;
            var errors = new List<string>();
            var newEntries = new List<JournalEntry>();

            foreach (var tx in transactions)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    if (existingByTx.TryGetValue(tx.Id, out var existingJe))
                    {
                        if (RepointTransactionEntry(existingJe, tx, accounts, accountsByNumber, categoriesByName, fallbackGamingRevenue, fallbackItemRevenue))
                            repointed++;
                        else
                            skipped++;
                    }
                    else
                    {
                        var je = BuildTransactionEntry(tx, accounts, accountsByNumber, cashAccount, fallbackGamingRevenue, fallbackItemRevenue, ref nextSeq);
                        if (je == null)
                        {
                            failed++;
                            errors.Add($"Tx#{tx.Id}: could not build entry (missing data or no revenue lines)");
                            continue;
                        }
                        newEntries.Add(je);
                        created++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Tx#{tx.Id}: {ex.GetBaseException().Message}");
                    _logger.LogError(ex, "Backfill failed for transaction {TxId}", tx.Id);
                }
            }

            foreach (var je in newEntries)
            {
                await _journalRepo.AddAsync(je, ct);
            }

            await _uow.SaveChangesAsync(ct);

            sw.Stop();
            _logger.LogInformation(
                "Transaction backfill done in {Elapsed}ms. Total={Total}, Created={Created}, Repointed={Repointed}, Skipped={Skipped}, Failed={Failed}",
                sw.ElapsedMilliseconds, transactions.Count, created, repointed, skipped, failed);

            return new BackfillResultDto(
                Total: transactions.Count,
                Success: created + repointed,
                Failed: failed,
                Errors: errors);
        }

        // ============================================
        // EXPENSES — fast bulk + in-place re-route
        // ============================================

        public async Task<BackfillResultDto> BackfillExpensesAsync(CancellationToken ct = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var expenses = await _expenseRepo.Query()
                .Include(e => e.Category)
                    .ThenInclude(c => c.Account)
                .AsNoTracking()
                .ToListAsync(ct);

            if (expenses.Count == 0)
                return new BackfillResultDto(0, 0, 0, new List<string>());

            var accounts = await _accountRepo.Query(asNoTracking: false)
                .Include(a => a.AccountType)
                .ToDictionaryAsync(a => a.Id, ct);

            var accountsByNumber = accounts.Values
                .Where(a => a.IsActive)
                .ToDictionary(a => a.AccountNumber, a => a);

            if (!accountsByNumber.TryGetValue("1000", out var cashAccount))
                return new BackfillResultDto(0, 0, expenses.Count,
                    new List<string> { "Cash account (1000) not found" });

            accountsByNumber.TryGetValue("5900", out var miscExpenseAccount);

            var expenseIds = expenses.Select(e => e.Id).ToList();
            var existingEntries = await _journalRepo.Query(asNoTracking: false)
                .Include(je => je.Lines)
                .Where(je => je.ReferenceType == "Expense"
                          && je.ReferenceId != null
                          && expenseIds.Contains(je.ReferenceId.Value))
                .ToListAsync(ct);
            var existingByExpense = existingEntries
                .GroupBy(je => je.ReferenceId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            var nextSeq = await ComputeNextEntryNumberSequenceAsync(ct);

            int created = 0, repointed = 0, skipped = 0, failed = 0;
            var errors = new List<string>();
            var newEntries = new List<JournalEntry>();

            foreach (var expense in expenses)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var expectedAccount = ResolveExpenseAccount(expense, accountsByNumber, miscExpenseAccount);
                    if (expectedAccount == null)
                    {
                        failed++;
                        errors.Add($"Expense#{expense.Id}: no suitable expense account");
                        continue;
                    }

                    if (existingByExpense.TryGetValue(expense.Id, out var jes))
                    {
                        // Per user direction: do NOT restructure old single-entry JEs into
                        // the new monthly format. Just re-point the debit lines so the GL
                        // reflects the currently-mapped account.
                        if (RepointExpenseEntries(jes, expectedAccount, accounts))
                            repointed++;
                        else
                            skipped++;
                    }
                    else
                    {
                        var monthly = BuildMonthlyExpenseEntries(expense, expectedAccount, cashAccount, accounts, ref nextSeq);
                        if (monthly.Count == 0)
                        {
                            failed++;
                            errors.Add($"Expense#{expense.Id}: no allocations produced");
                            continue;
                        }
                        newEntries.AddRange(monthly);
                        created++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Expense#{expense.Id}: {ex.GetBaseException().Message}");
                    _logger.LogError(ex, "Backfill failed for expense {ExpenseId}", expense.Id);
                }
            }

            foreach (var je in newEntries)
            {
                await _journalRepo.AddAsync(je, ct);
            }

            await _uow.SaveChangesAsync(ct);

            sw.Stop();
            _logger.LogInformation(
                "Expense backfill done in {Elapsed}ms. Total={Total}, Created={Created}, Repointed={Repointed}, Skipped={Skipped}, Failed={Failed}",
                sw.ElapsedMilliseconds, expenses.Count, created, repointed, skipped, failed);

            return new BackfillResultDto(
                Total: expenses.Count,
                Success: created + repointed,
                Failed: failed,
                Errors: errors);
        }

        // ============================================
        // SINGLE-CATEGORY BACKFILL (still used elsewhere)
        // ============================================

        public async Task<BackfillResultDto> BackfillCategoryAsync(int categoryId, CancellationToken ct = default)
        {
            var postedExpenseIds = await _journalRepo.Query()
                .Where(je => je.ReferenceType == "Expense" && je.ReferenceId != null)
                .Select(je => je.ReferenceId!.Value)
                .ToListAsync(ct);

            var missingExpenseIds = await _expenseRepo.Query()
                .Where(e => e.FK_CategoryId == categoryId && !postedExpenseIds.Contains(e.Id))
                .Select(e => e.Id)
                .ToListAsync(ct);

            int success = 0, failed = 0;
            var errors = new List<string>();

            foreach (var expenseId in missingExpenseIds)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var result = await _journalService.CreateJournalEntryFromExpenseAsync(expenseId, ct);
                    if (result.Success)
                        success++;
                    else
                    {
                        failed++;
                        errors.Add($"Expense#{expenseId}: {result.Message}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Expense#{expenseId}: exception - {ex.Message}");
                }
            }

            return new BackfillResultDto(
                Total: missingExpenseIds.Count,
                Success: success,
                Failed: failed,
                Errors: errors);
        }

        // ============================================
        // HELPERS
        // ============================================

        private async Task<int> ComputeNextEntryNumberSequenceAsync(CancellationToken ct)
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"JE-{year}-";
            var lastEntry = await _journalRepo.Query()
                .Where(e => e.EntryNumber.StartsWith(prefix))
                .OrderByDescending(e => e.EntryNumber)
                .Select(e => e.EntryNumber)
                .FirstOrDefaultAsync(ct);

            if (lastEntry == null) return 1;
            var trailing = lastEntry.Substring(prefix.Length);
            return int.TryParse(trailing, out var s) ? s + 1 : 1;
        }

        private static string FormatEntryNumber(int sequence)
        {
            var year = DateTime.UtcNow.Year;
            return $"JE-{year}-{sequence:D5}";
        }

        private static decimal LineEffectOn(Account acct, decimal debit, decimal credit)
        {
            return acct.AccountType.NormalBalance == "Debit"
                ? debit - credit
                : credit - debit;
        }

        // ── Transactions ────────────────────────────────────────────────────────

        private bool RepointTransactionEntry(
            JournalEntry je,
            TransactionRecord tx,
            Dictionary<int, Account> accounts,
            Dictionary<string, Account> accountsByNumber,
            Dictionary<string, Category> categoriesByName,
            Account? fallbackGamingRevenue,
            Account? fallbackItemRevenue)
        {
            bool changed = false;

            foreach (var line in je.Lines)
            {
                // Cash debit is always 1000; nothing to re-route there.
                if (line.DebitAmount > 0) continue;

                int? expectedAccountId = null;

                if (tx.GameId != null)
                {
                    // Single revenue line for the game's category.
                    var cat = tx.Game?.Category;
                    expectedAccountId = cat?.AccountId ?? fallbackGamingRevenue?.Id;
                }
                else
                {
                    // Multiple revenue lines — match by category name in the line description.
                    var categoryName = ExtractCategoryNameFromDescription(line.Description);
                    if (categoryName != null
                        && categoriesByName.TryGetValue(categoryName.Trim().ToLowerInvariant(), out var cat))
                    {
                        expectedAccountId = cat.AccountId ?? fallbackItemRevenue?.Id;
                    }
                    else
                    {
                        // Couldn't identify the category from the description; leave it alone.
                        continue;
                    }
                }

                if (!expectedAccountId.HasValue || expectedAccountId.Value == line.AccountId)
                    continue;

                if (!accounts.TryGetValue(line.AccountId, out var oldAcct) ||
                    !accounts.TryGetValue(expectedAccountId.Value, out var newAcct))
                    continue;

                if (je.IsPosted && !je.IsVoided)
                {
                    var oldEffect = LineEffectOn(oldAcct, line.DebitAmount, line.CreditAmount);
                    var newEffect = LineEffectOn(newAcct, line.DebitAmount, line.CreditAmount);
                    oldAcct.CurrentBalance -= oldEffect;
                    newAcct.CurrentBalance += newEffect;
                }

                line.AccountId = expectedAccountId.Value;
                changed = true;
            }

            return changed;
        }

        private static string? ExtractCategoryNameFromDescription(string? description)
        {
            if (string.IsNullOrWhiteSpace(description)) return null;
            // Patterns we wrote: "Sales - {CategoryName}", "Gaming revenue - {GameName}".
            // Only the "Sales - " pattern carries a Category we can look up; gaming
            // revenue is keyed via tx.Game.Category directly.
            const string prefix = "Sales - ";
            if (description.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return description.Substring(prefix.Length);
            return null;
        }

        private JournalEntry? BuildTransactionEntry(
            TransactionRecord tx,
            Dictionary<int, Account> accounts,
            Dictionary<string, Account> accountsByNumber,
            Account cashAccount,
            Account? fallbackGamingRevenue,
            Account? fallbackItemRevenue,
            ref int nextSeq)
        {
            var lines = new List<JournalEntryLine>
            {
                new JournalEntryLine
                {
                    AccountId = cashAccount.Id,
                    DebitAmount = tx.TotalPrice,
                    CreditAmount = 0,
                    Description = "Cash received",
                    LineNumber = 1,
                    CreatedAt = DateTime.UtcNow
                }
            };

            if (tx.GameId != null)
            {
                var revenueAccount = tx.Game?.Category?.AccountId is int catAcctId && accounts.TryGetValue(catAcctId, out var mapped)
                    ? mapped
                    : fallbackGamingRevenue;

                if (revenueAccount == null) return null;

                lines.Add(new JournalEntryLine
                {
                    AccountId = revenueAccount.Id,
                    DebitAmount = 0,
                    CreditAmount = tx.TotalPrice,
                    Description = $"Gaming revenue - {tx.Game?.Name ?? "Game"}",
                    LineNumber = 2,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else if (tx.TransactionItems.Any())
            {
                var itemsByCat = tx.TransactionItems
                    .Where(ti => ti.Item != null)
                    .GroupBy(ti => ti.Item!.Category)
                    .Select(g => new
                    {
                        Category = g.Key,
                        FullPrice = g.Sum(ti => ti.Item!.Price * ti.Quantity)
                    })
                    .ToList();

                var fullTotal = itemsByCat.Sum(x => x.FullPrice);
                if (fullTotal == 0 || itemsByCat.Count == 0) return null;

                decimal totalCredited = 0m;
                int lineNum = 2;
                foreach (var catGroup in itemsByCat)
                {
                    var proportion = catGroup.FullPrice / fullTotal;
                    var lineAmount = Math.Round(tx.TotalPrice * proportion, 2);

                    var revenueAccount = catGroup.Category?.AccountId is int catAcctId && accounts.TryGetValue(catAcctId, out var mapped)
                        ? mapped
                        : fallbackItemRevenue;

                    if (revenueAccount == null) continue;

                    lines.Add(new JournalEntryLine
                    {
                        AccountId = revenueAccount.Id,
                        DebitAmount = 0,
                        CreditAmount = lineAmount,
                        Description = $"Sales - {catGroup.Category?.Name ?? "Item"}",
                        LineNumber = lineNum++,
                        CreatedAt = DateTime.UtcNow
                    });

                    totalCredited += lineAmount;
                }

                if (totalCredited == 0) return null;

                // Absorb rounding drift on the last credit line so totals match exactly.
                var drift = tx.TotalPrice - totalCredited;
                if (Math.Abs(drift) > 0)
                {
                    var lastCredit = lines.Where(l => l.CreditAmount > 0).LastOrDefault();
                    if (lastCredit != null)
                        lastCredit.CreditAmount += drift;
                }
            }
            else
            {
                return null;
            }

            var entry = new JournalEntry
            {
                EntryNumber = FormatEntryNumber(nextSeq++),
                EntryDate = DateTime.SpecifyKind(tx.CreatedOn, DateTimeKind.Utc),
                Description = $"Transaction #{tx.Id} - Sale",
                ReferenceType = "Transaction",
                ReferenceId = tx.Id,
                TotalAmount = tx.TotalPrice,
                IsPosted = true,
                PostedAt = DateTime.UtcNow,
                IsVoided = false,
                CreatedAt = DateTime.UtcNow,
                Lines = lines
            };

            // Apply posted balance impact.
            foreach (var line in lines)
            {
                if (!accounts.TryGetValue(line.AccountId, out var acct)) continue;
                acct.CurrentBalance += LineEffectOn(acct, line.DebitAmount, line.CreditAmount);
            }

            return entry;
        }

        // ── Expenses ────────────────────────────────────────────────────────────

        private static Account? ResolveExpenseAccount(
            Expense expense,
            Dictionary<string, Account> accountsByNumber,
            Account? miscExpenseAccount)
        {
            if (expense.Category?.Account is { IsActive: true } mapped) return mapped;

            // Keyword fallback (mirrors JournalService.DetermineExpenseAccountAsync).
            var name = expense.Category?.Name?.ToLowerInvariant() ?? string.Empty;
            string? acctNumber = null;
            if (name.Contains("rent")) acctNumber = "5100";
            else if (name.Contains("utilit") || name.Contains("electric")) acctNumber = "5200";
            else if (name.Contains("internet") || name.Contains("telecom")) acctNumber = "5300";
            else if (name.Contains("salary") || name.Contains("wage")) acctNumber = "5400";
            else if (name.Contains("marketing") || name.Contains("social")) acctNumber = "5500";
            else if (name.Contains("maintenance") || name.Contains("repair")) acctNumber = "5600";
            else if (name.Contains("office") || name.Contains("supplies")) acctNumber = "5800";

            if (acctNumber != null && accountsByNumber.TryGetValue(acctNumber, out var byKeyword))
                return byKeyword;

            return miscExpenseAccount;
        }

        private bool RepointExpenseEntries(
            List<JournalEntry> jes,
            Account expectedAccount,
            Dictionary<int, Account> accounts)
        {
            bool changed = false;

            foreach (var je in jes)
            {
                foreach (var line in je.Lines)
                {
                    // Only re-point the expense-side debit line; the cash credit stays on 1000.
                    if (line.DebitAmount <= 0) continue;
                    if (line.AccountId == expectedAccount.Id) continue;

                    if (!accounts.TryGetValue(line.AccountId, out var oldAcct)) continue;

                    if (je.IsPosted && !je.IsVoided)
                    {
                        var oldEffect = LineEffectOn(oldAcct, line.DebitAmount, line.CreditAmount);
                        var newEffect = LineEffectOn(expectedAccount, line.DebitAmount, line.CreditAmount);
                        oldAcct.CurrentBalance -= oldEffect;
                        expectedAccount.CurrentBalance += newEffect;
                    }

                    line.AccountId = expectedAccount.Id;
                    changed = true;
                }
            }

            return changed;
        }

        private static List<JournalEntry> BuildMonthlyExpenseEntries(
            Expense expense,
            Account expenseAccount,
            Account cashAccount,
            Dictionary<int, Account> accounts,
            ref int nextSeq)
        {
            var allocations = BuildMonthlyAllocations(expense.Amount, expense.FromDate, expense.ToDate);
            var currentMonthStart = FirstOfMonth(DateTime.UtcNow);
            var result = new List<JournalEntry>(allocations.Count);

            foreach (var (monthStart, allocation) in allocations)
            {
                var entryDate = DateTime.SpecifyKind(monthStart, DateTimeKind.Utc);
                var isPosted = entryDate <= currentMonthStart;
                var monthLabel = monthStart.ToString("MMM yyyy");
                var description = string.IsNullOrWhiteSpace(expense.Comment)
                    ? $"{expense.Category?.Name ?? "Expense"} - {monthLabel}"
                    : $"{expense.Comment} - {monthLabel}";

                var entry = new JournalEntry
                {
                    EntryNumber = FormatEntryNumber(nextSeq++),
                    EntryDate = entryDate,
                    Description = description,
                    ReferenceType = "Expense",
                    ReferenceId = expense.Id,
                    TotalAmount = allocation,
                    IsPosted = isPosted,
                    PostedAt = isPosted ? DateTime.UtcNow : null,
                    IsVoided = false,
                    CreatedAt = DateTime.UtcNow,
                    Lines = new List<JournalEntryLine>
                    {
                        new JournalEntryLine
                        {
                            AccountId = expenseAccount.Id,
                            DebitAmount = allocation,
                            CreditAmount = 0,
                            Description = description,
                            LineNumber = 1,
                            CreatedAt = DateTime.UtcNow
                        },
                        new JournalEntryLine
                        {
                            AccountId = cashAccount.Id,
                            DebitAmount = 0,
                            CreditAmount = allocation,
                            Description = "Cash paid",
                            LineNumber = 2,
                            CreatedAt = DateTime.UtcNow
                        }
                    }
                };

                if (isPosted)
                {
                    expenseAccount.CurrentBalance += LineEffectOn(expenseAccount, allocation, 0);
                    cashAccount.CurrentBalance += LineEffectOn(cashAccount, 0, allocation);
                }

                result.Add(entry);
            }

            return result;
        }

        private static DateTime FirstOfMonth(DateTime d) =>
            new DateTime(d.Year, d.Month, 1, 0, 0, 0, DateTimeKind.Utc);

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
                decimal allocation = (i == n - 1) ? amount - allocatedSoFar : monthly;
                if (i != n - 1) allocatedSoFar += monthly;
                result.Add((monthStart, allocation));
            }

            return result;
        }
    }
}
