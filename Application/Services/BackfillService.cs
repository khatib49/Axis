using Application.DTOs;
using Application.IServices;
using Domain.Entities;
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
        private readonly IJournalService _journalService;
        private readonly ILogger<BackfillService> _logger;

        public BackfillService(
            IBaseRepository<TransactionRecord> txRepo,
            IBaseRepository<Expense> expenseRepo,
            IBaseRepository<JournalEntry> journalRepo,
            IJournalService journalService,
            ILogger<BackfillService> logger)
        {
            _txRepo = txRepo;
            _expenseRepo = expenseRepo;
            _journalRepo = journalRepo;
            _journalService = journalService;
            _logger = logger;
        }

        public async Task<BackfillResultDto> BackfillTransactionsAsync(CancellationToken ct = default)
        {
            // Get all completed transaction IDs that have NO journal entry
            var postedTxIds = await _journalRepo.Query()
                .Where(je => je.ReferenceType == "Transaction" && je.ReferenceId != null)
                .Select(je => je.ReferenceId!.Value)
                .ToListAsync(ct);

            var missingTxIds = await _txRepo.Query()
                .Where(t => t.StatusId == 6 && t.TotalPrice > 0 && !postedTxIds.Contains(t.Id))
                .Select(t => t.Id)
                .ToListAsync(ct);

            int success = 0, failed = 0;
            var errors = new List<string>();

            foreach (var txId in missingTxIds)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var result = await _journalService.CreateJournalEntryFromTransactionAsync(txId, ct);
                    if (result.Success)
                        success++;
                    else
                    {
                        failed++;
                        errors.Add($"Tx#{txId}: {result.Message}");
                        _logger.LogWarning("Backfill failed for transaction {TxId}: {Error}", txId, result.Message);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Tx#{txId}: exception - {ex.Message}");
                    _logger.LogError(ex, "Backfill exception for transaction {TxId}", txId);
                }
            }

            _logger.LogInformation(
                "Transaction backfill complete. Total={Total}, Success={Success}, Failed={Failed}",
                missingTxIds.Count, success, failed);

            return new BackfillResultDto(
                Total: missingTxIds.Count,
                Success: success,
                Failed: failed,
                Errors: errors
            );
        }

        public async Task<BackfillResultDto> BackfillExpensesAsync(CancellationToken ct = default)
        {
            var postedExpenseIds = await _journalRepo.Query()
                .Where(je => je.ReferenceType == "Expense" && je.ReferenceId != null)
                .Select(je => je.ReferenceId!.Value)
                .ToListAsync(ct);

            var missingExpenseIds = await _expenseRepo.Query()
                .Where(e => !postedExpenseIds.Contains(e.Id))
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
                        _logger.LogWarning("Backfill failed for expense {ExpenseId}: {Error}", expenseId, result.Message);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"Expense#{expenseId}: exception - {ex.Message}");
                    _logger.LogError(ex, "Backfill exception for expense {ExpenseId}", expenseId);
                }
            }

            _logger.LogInformation(
                "Expense backfill complete. Total={Total}, Success={Success}, Failed={Failed}",
                missingExpenseIds.Count, success, failed);

            return new BackfillResultDto(
                Total: missingExpenseIds.Count,
                Success: success,
                Failed: failed,
                Errors: errors
            );
        }

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
                Errors: errors
            );
        }
    }
}
