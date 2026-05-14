using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class ExpenseService : IExpenseService
    {
        private readonly IBaseRepository<Expense> _expenseRepo;
        private readonly IBaseRepository<ExpenseCategory> _catRepo;
        private readonly IUnitOfWork _uow;
        private readonly IJournalService _journalService; 
        private readonly ILogger<ExpenseService> _logger;

        public ExpenseService(IBaseRepository<Expense> expenseRepo, IBaseRepository<ExpenseCategory> catRepo,
            IUnitOfWork unitOfWork, IJournalService journalService, ILogger<ExpenseService> logger)
        {
            _expenseRepo = expenseRepo;
            _catRepo = catRepo;
            _uow = unitOfWork;
            _logger = logger;
            _journalService = journalService;
        }

        // In ExpenseService.cs - Update CreateJournalEntryFromExpenseAsync call
        public async Task<ExpenseDto> CreateAsync(
            ExpenseCreateDto dto,
            int? createdBy,
            CancellationToken ct)
        {
            await EnsureCategoryExists(dto.CategoryId, ct);
            ValidateDates(dto.FromDate, dto.ToDate);
            if (dto.Amount <= 0)
                throw new ArgumentException("Amount must be greater than 0.");

            var entity = new Expense
            {
                FK_CategoryId = dto.CategoryId,
                Amount = dto.Amount,
                PaymentMethod = dto.PaymentMethod,
                Comment = dto.Comment,
                FromDate = dto.FromDate.Date,
                ToDate = dto.ToDate.Date,
                CreatedBy = createdBy,
                CreatedOn = DateTime.UtcNow,
            };

            await _expenseRepo.AddAsync(entity, ct);
            await _uow.SaveChangesAsync(ct);

            try
            {
                var journalResult = await _journalService
                    .CreateJournalEntryFromExpenseAsync(entity.Id, ct);

                if (journalResult.Success)
                {
                    _logger.LogInformation(
                        "Journal entry {EntryNumber} created for expense {ExpenseId}",
                        journalResult.Data?.EntryNumber,
                        entity.Id);
                }
                else
                {
                    _logger.LogWarning(
                        "Failed to create journal entry for expense {ExpenseId}: {Error}",
                        entity.Id,
                        journalResult.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Exception creating journal entry for expense {ExpenseId}",
                    entity.Id);
            }

            var catName = await _catRepo.Query()
                .Where(c => c.Id == entity.FK_CategoryId)
                .Select(c => c.Name)
                .FirstAsync(ct);

            return Map(entity, catName);
        }

        public async Task<ExpenseDto> UpdateAsync(int id, ExpenseUpdateDto dto, CancellationToken ct)
        {
            await EnsureCategoryExists(dto.CategoryId, ct);
            ValidateDates(dto.FromDate, dto.ToDate);
            if (dto.Amount <= 0) throw new ArgumentException("Amount must be greater than 0.");

            var entity = await _expenseRepo.Query().FirstOrDefaultAsync(e => e.Id == id, ct)
                         ?? throw new KeyNotFoundException("Expense not found.");

            entity.FK_CategoryId = dto.CategoryId;
            entity.Amount = dto.Amount;
            entity.PaymentMethod = dto.PaymentMethod;
            entity.Comment = dto.Comment;
            entity.FromDate = dto.FromDate.Date;
            entity.ToDate = dto.ToDate.Date;

            _expenseRepo.Update(entity);

            await _uow.SaveChangesAsync(ct);

            try
            {
                await _journalService.DeleteJournalEntriesForExpenseAsync(id, ct);
                await _journalService.CreateJournalEntryFromExpenseAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to regenerate journal entries after updating expense {ExpenseId}",
                    id);
            }

            var catName = (await _catRepo.Query().Where(c => c.Id == entity.FK_CategoryId)
                                    .Select(c => c.Name).FirstAsync(ct));

            return Map(entity, catName);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct)
        {
            var entity = await _expenseRepo.Query().FirstOrDefaultAsync(e => e.Id == id, ct);
            if (entity is null) return false;

            try
            {
                await _journalService.DeleteJournalEntriesForExpenseAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to delete journal entries for expense {ExpenseId}",
                    id);
            }

            _expenseRepo.Remove(entity);

            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<RegenerateJournalsResultDto> RegenerateAllJournalsAsync(CancellationToken ct)
        {
            var processed = 0;
            var succeeded = 0;
            var failed = 0;
            var errors = new List<string>();

            var allIds = await _expenseRepo.Query()
                .OrderBy(e => e.Id)
                .Select(e => e.Id)
                .ToListAsync(ct);

            foreach (var expenseId in allIds)
            {
                processed++;
                var transactionStarted = false;
                try
                {
                    await _uow.BeginTransactionAsync(ct);
                    transactionStarted = true;

                    await _journalService.DeleteJournalEntriesForExpenseAsync(expenseId, ct);
                    var result = await _journalService.CreateJournalEntryFromExpenseAsync(expenseId, ct);

                    if (result.Success)
                    {
                        await _uow.CommitAsync(ct);
                        transactionStarted = false;
                        succeeded++;
                    }
                    else
                    {
                        await _uow.RollbackAsync(ct);
                        transactionStarted = false;
                        failed++;
                        errors.Add($"Expense {expenseId}: {result.Message}");
                        _logger.LogWarning(
                            "Regenerate failed for expense {ExpenseId}, rolled back: {Message}",
                            expenseId,
                            result.Message);
                    }
                }
                catch (Exception ex)
                {
                    if (transactionStarted)
                    {
                        try { await _uow.RollbackAsync(ct); }
                        catch (Exception rbEx)
                        {
                            _logger.LogError(rbEx,
                                "Rollback failed for expense {ExpenseId}",
                                expenseId);
                        }
                    }
                    failed++;
                    errors.Add($"Expense {expenseId}: {ex.GetBaseException().Message}");
                    _logger.LogError(ex,
                        "Error regenerating journals for expense {ExpenseId}",
                        expenseId);
                }
                finally
                {
                    // Each expense is processed in isolation; clear tracked state so the
                    // next iteration sees fresh DB values and a poisoned tracker after a
                    // rollback cannot leak into subsequent saves.
                    _uow.ResetChangeTracker();
                }
            }

            return new RegenerateJournalsResultDto(processed, succeeded, failed, errors);
        }

        public async Task<ExpenseDto?> GetByIdAsync(int id, CancellationToken ct)
        {
            var q = from e in _expenseRepo.Query()
                    join c in _catRepo.Query() on e.FK_CategoryId equals c.Id
                    where e.Id == id
                    select new ExpenseDto(
                        e.Id, e.FK_CategoryId, c.Name, e.Amount, e.PaymentMethod, e.Comment,
                        e.FromDate, e.ToDate, e.CreatedBy, e.CreatedOn
                    );

            return await q.FirstOrDefaultAsync(ct);
        }

        public async Task<PagedExpensesResult> QueryAsync(ExpenseFilter filter, CancellationToken ct)
        {
            var q = from e in _expenseRepo.Query()
                    join c in _catRepo.Query() on e.FK_CategoryId equals c.Id
                    select new { e, c.Name };

            if (filter.From.HasValue)
                q = q.Where(x => x.e.ToDate >= filter.From.Value.Date);  // overlaps range

            if (filter.To.HasValue)
                q = q.Where(x => x.e.FromDate <= filter.To.Value.Date);  // overlaps range

            if (filter.CategoryId.HasValue)
                q = q.Where(x => x.e.FK_CategoryId == filter.CategoryId.Value);

            var totalCount = await q.CountAsync(ct);
            var totalAmountAll = await q.SumAsync(x => (decimal?)x.e.Amount, ct) ?? 0m;

            var itemsQ = q
                .OrderByDescending(x => x.e.FromDate)
                .Skip((filter.Page - 1) * filter.PageSize)
                .Take(filter.PageSize);

            var items = await itemsQ
                .Select(x => new ExpenseDto(
                    x.e.Id, x.e.FK_CategoryId, x.Name, x.e.Amount, x.e.PaymentMethod, x.e.Comment,
                    x.e.FromDate, x.e.ToDate, x.e.CreatedBy, x.e.CreatedOn
                ))
                .ToListAsync(ct);

            var totalAmountPage = items.Sum(i => i.Amount);

            return new PagedExpensesResult(
                filter.Page, filter.PageSize, totalCount, totalAmountPage, totalAmountAll, items
            );
        }

        private static void ValidateDates(DateTime from, DateTime to)
        {
            if (from.Date > to.Date)
                throw new ArgumentException("FromDate must be on or before ToDate.");
        }

        private async Task EnsureCategoryExists(int categoryId, CancellationToken ct)
        {
            var exists = await _catRepo.Query().AnyAsync(c => c.Id == categoryId, ct);
            if (!exists) throw new ArgumentException("Invalid CategoryId.");
        }

        private static ExpenseDto Map(Expense e, string categoryName) =>
            new(e.Id, e.FK_CategoryId, categoryName, e.Amount, e.PaymentMethod, e.Comment,
                e.FromDate, e.ToDate, e.CreatedBy, e.CreatedOn);
    }
}
