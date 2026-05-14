using Application.DTOs;

namespace Application.IServices
{

    public interface IBackfillService
    {
        Task<BackfillResultDto> BackfillTransactionsAsync(CancellationToken ct = default);
        Task<BackfillResultDto> BackfillExpensesAsync(CancellationToken ct = default);
        Task<BackfillResultDto> BackfillCategoryAsync(int categoryId, CancellationToken ct = default);
    }
}
