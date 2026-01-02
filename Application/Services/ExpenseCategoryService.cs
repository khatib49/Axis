using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class ExpenseCategoryService : IExpenseCategoryService
    {
        private readonly IBaseRepository<ExpenseCategory> _catRepo;
        private readonly IBaseRepository<Expense> _expenseRepo;
        private readonly IBaseRepository<Account> _accountRepo;  // NEW
        private readonly IUnitOfWork _uow;

        public ExpenseCategoryService(
            IBaseRepository<ExpenseCategory> catRepo,
            IBaseRepository<Expense> expenseRepo,
            IBaseRepository<Account> accountRepo,  // NEW
            IUnitOfWork unitOfWork)
        {
            _catRepo = catRepo;
            _expenseRepo = expenseRepo;
            _accountRepo = accountRepo;  // NEW
            _uow = unitOfWork;
        }

        public async Task<ExpenseCategoryDto> CreateAsync(
            ExpenseCategoryCreateDto dto,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ArgumentException("Name is required.");

            var exists = await _catRepo.Query().AnyAsync(c => c.Name == dto.Name, ct);
            if (exists)
                throw new InvalidOperationException("Category name already exists.");

            // Validate account if provided
            if (dto.AccountId.HasValue)
            {
                var accountExists = await _accountRepo.Query()
                    .AnyAsync(a => a.Id == dto.AccountId.Value && a.IsActive, ct);
                if (!accountExists)
                    throw new ArgumentException("Invalid account ID.");
            }

            var entity = new ExpenseCategory
            {
                Name = dto.Name.Trim(),
                Description = dto.Description,
                AccountId = dto.AccountId  // NEW
            };

            await _catRepo.AddAsync(entity, ct);
            await _uow.SaveChangesAsync(ct);

            return await GetDtoAsync(entity.Id, ct);
        }

        public async Task<IReadOnlyList<ExpenseCategoryDto>> ListAsync(CancellationToken ct)
        {
            return await _catRepo.Query()
                .Include(c => c.Account)  // NEW
                .OrderBy(c => c.Name)
                .Select(c => new ExpenseCategoryDto(
                    c.Id,
                    c.Name,
                    c.Description,
                    c.AccountId,
                    c.Account != null ? c.Account.AccountNumber : null,
                    c.Account != null ? c.Account.AccountName : null
                ))
                .ToListAsync(ct);
        }

        public async Task<ExpenseCategoryDto> UpdateAsync(
            int id,
            ExpenseCategoryUpdateDto dto,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ArgumentException("Name is required.");

            var entity = await _catRepo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new KeyNotFoundException("Category not found.");

            var nameExists = await _catRepo.Query()
                .AnyAsync(c => c.Id != id && c.Name == dto.Name, ct);
            if (nameExists)
                throw new InvalidOperationException("Category name already exists.");

            // Validate account if provided
            if (dto.AccountId.HasValue)
            {
                var accountExists = await _accountRepo.Query()
                    .AnyAsync(a => a.Id == dto.AccountId.Value && a.IsActive, ct);
                if (!accountExists)
                    throw new ArgumentException("Invalid account ID.");
            }

            entity.Name = dto.Name.Trim();
            entity.Description = dto.Description;
            entity.AccountId = dto.AccountId;  // NEW

            _catRepo.Update(entity);
            await _uow.SaveChangesAsync(ct);

            return await GetDtoAsync(entity.Id, ct);
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            var entity = await _catRepo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new KeyNotFoundException("Category not found.");

            if (await _expenseRepo.Query().AnyAsync(e => e.FK_CategoryId == id, ct))
                throw new InvalidOperationException(
                    "Cannot delete a category that is used by expenses.");

            _catRepo.Remove(entity);
            await _uow.SaveChangesAsync(ct);
        }

        // Helper method to get full DTO
        private async Task<ExpenseCategoryDto> GetDtoAsync(int id, CancellationToken ct)
        {
            var category = await _catRepo.Query()
                .Include(c => c.Account)
                .FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new KeyNotFoundException("Category not found.");

            return new ExpenseCategoryDto(
                category.Id,
                category.Name,
                category.Description,
                category.AccountId,
                category.Account?.AccountNumber,
                category.Account?.AccountName
            );
        }
    }
}