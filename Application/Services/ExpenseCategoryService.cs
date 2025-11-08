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

        public ExpenseCategoryService(IBaseRepository<ExpenseCategory> catRepo)
        {
            _catRepo = catRepo;
        }

        public async Task<ExpenseCategoryDto> CreateAsync(ExpenseCategoryCreateDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                throw new ArgumentException("Name is required.");

            var exists = await _catRepo.Query().AnyAsync(c => c.Name == dto.Name, ct);
            if (exists) throw new InvalidOperationException("Category name already exists.");

            var entity = new ExpenseCategory { Name = dto.Name.Trim(), Description = dto.Description };
            await _catRepo.AddAsync(entity, ct);

            return new ExpenseCategoryDto(entity.Id, entity.Name, entity.Description);
        }

        public async Task<IReadOnlyList<ExpenseCategoryDto>> ListAsync(CancellationToken ct)
        {
            return await _catRepo.Query()
                .OrderBy(c => c.Name)
                .Select(c => new ExpenseCategoryDto(c.Id, c.Name, c.Description))
                .ToListAsync(ct);
        }
    }
}
