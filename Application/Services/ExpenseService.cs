using Application.DTOs;
using Application.IServices;
using Application.Mapping;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class ExpenseService : IExpenseService
    {
        private readonly IBaseRepository<Expense> _expenseRepo;
        private readonly IBaseRepository<ExpenseCategory> _catRepo;
        private readonly IUnitOfWork _uow;

        public ExpenseService(IBaseRepository<Expense> expenseRepo, IBaseRepository<ExpenseCategory> catRepo, IUnitOfWork unitOfWork)
        {
            _expenseRepo = expenseRepo;
            _catRepo = catRepo;
            _uow = unitOfWork;
        }

        public async Task<ExpenseDto> CreateAsync(ExpenseCreateDto dto, int? createdBy, CancellationToken ct)
        {
            await EnsureCategoryExists(dto.CategoryId, ct);
            ValidateDates(dto.FromDate, dto.ToDate);
            if (dto.Amount <= 0) throw new ArgumentException("Amount must be greater than 0.");

            var entity = new Expense
            {
                FK_CategoryId = dto.CategoryId,
                Amount = dto.Amount,
                PaymentMethod = dto.PaymentMethod,
                Comment = dto.Comment,
                FromDate = dto.FromDate.Date,
                ToDate = dto.ToDate.Date,
                CreatedBy = createdBy,
                CreatedOn = DateTime.UtcNow
            };

            await _expenseRepo.AddAsync(entity, ct);

            await _uow.SaveChangesAsync(ct);
            // Load category name for DTO
            var catName = (await _catRepo.Query().Where(c => c.Id == entity.FK_CategoryId)
                                    .Select(c => c.Name).FirstAsync(ct));

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
            var catName = (await _catRepo.Query().Where(c => c.Id == entity.FK_CategoryId)
                                    .Select(c => c.Name).FirstAsync(ct));

            return Map(entity, catName);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct)
        {
            var entity = await _expenseRepo.Query().FirstOrDefaultAsync(e => e.Id == id, ct);
            if (entity is null) return false;

            _expenseRepo.Remove(entity);

            await _uow.SaveChangesAsync(ct);
            return true;
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
