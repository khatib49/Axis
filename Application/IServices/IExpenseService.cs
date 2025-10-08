using Application.DTOs;

namespace Application.IServices
{
    public interface IExpenseService
    {
        Task<ExpenseDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<ExpenseDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<ExpenseDto> CreateAsync(ExpenseCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, ExpenseUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
