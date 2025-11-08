using Application.DTOs;

namespace Application.IServices
{
    public interface IExpenseService
    {
        Task<ExpenseDto> CreateAsync(ExpenseCreateDto dto, int? createdBy, CancellationToken ct);
        Task<ExpenseDto> UpdateAsync(int id, ExpenseUpdateDto dto, CancellationToken ct);
        Task<bool> DeleteAsync(int id, CancellationToken ct);
        Task<ExpenseDto?> GetByIdAsync(int id, CancellationToken ct);
        Task<PagedExpensesResult> QueryAsync(ExpenseFilter filter, CancellationToken ct);

    }
}
