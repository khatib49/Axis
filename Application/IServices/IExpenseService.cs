using Application.DTOs;

namespace Application.IServices
{
    public interface IExpenseService
    {
        Task<ExpenseDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<PaginatedResponse<ExpenseDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<ExpenseDto> CreateAsync(ExpenseCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, ExpenseUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
