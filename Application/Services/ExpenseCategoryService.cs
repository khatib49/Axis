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
        private readonly IUnitOfWork _uow;

        public ExpenseCategoryService(IBaseRepository<ExpenseCategory> catRepo, IBaseRepository<Expense> expenseRepo, IUnitOfWork unitOfWork)
        {
            _catRepo = catRepo;
            _expenseRepo = expenseRepo;
            _uow = unitOfWork;
        }

        public async Task<ExpenseCategoryDto> CreateAsync(ExpenseCategoryCreateDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ArgumentException("Name is required.");

            var exists = await _catRepo.Query().AnyAsync(c => c.Name == dto.Name, ct);
            if (exists) throw new InvalidOperationException("Category name already exists.");

            var entity = new ExpenseCategory { Name = dto.Name.Trim(), Description = dto.Description };
            await _catRepo.AddAsync(entity, ct);
            await _uow.SaveChangesAsync(ct);

            return new ExpenseCategoryDto(entity.Id, entity.Name, entity.Description);
        }

        public async Task<IReadOnlyList<ExpenseCategoryDto>> ListAsync(CancellationToken ct)
        {
            return await _catRepo.Query()
                .OrderBy(c => c.Name)
                .Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.Description))
                .ToListAsync(ct);
        }

        public async Task<ExpenseCategoryDto> UpdateAsync(int id, ExpenseCategoryUpdateDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ArgumentException("Name is required.");

            // Load with tracking so changes are detected
            var entity = await _catRepo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new KeyNotFoundException("Category not found.");

            // Ensure unique name (excluding the same entity)
            var nameExists = await _catRepo.Query()
                .AnyAsync(c => c.Id != id && c.Name == dto.Name, ct);
            if (nameExists)
                throw new InvalidOperationException("Category name already exists.");

            entity.Name = dto.Name.Trim();
            entity.Description = dto.Description;

            // Explicitly mark as modified (safe even if being tracked)
            _catRepo.Update(entity);
            await _uow.SaveChangesAsync(ct);
            return new ExpenseCategoryDto(entity.Id, entity.Name, entity.Description);
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            // Load with tracking
            var entity = await _catRepo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new KeyNotFoundException("Category not found.");

            if (await _expenseRepo.Query().AnyAsync(e => e.FK_CategoryId == id, ct))
                throw new InvalidOperationException("Cannot delete a category that is used by expenses.");

            _catRepo.Remove(entity);
            await _uow.SaveChangesAsync(ct);
        }

    }
}
