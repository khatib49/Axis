using Application.DTOs;

namespace Application.IServices
{
    public interface IExpenseCategoryService
    {
        Task<ExpenseCategoryDto> CreateAsync(ExpenseCategoryCreateDto dto, CancellationToken ct);
        Task<IReadOnlyList<ExpenseCategoryDto>> ListAsync(CancellationToken ct);
        Task<ExpenseCategoryDto> UpdateAsync(int id, ExpenseCategoryUpdateDto dto, CancellationToken ct = default);
        Task DeleteAsync(int id, CancellationToken ct = default);
    }
}
