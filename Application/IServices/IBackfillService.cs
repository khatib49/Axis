using Application.DTOs;

namespace Application.IServices
{

    public interface IBackfillService
    {
        Task<BackfillResultDto> BackfillTransactionsAsync(CancellationToken ct = default);
        Task<BackfillResultDto> BackfillExpensesAsync(CancellationToken ct = default);
        Task<BackfillResultDto> BackfillCategoryAsync(int categoryId, CancellationToken ct = default);

        // Hangfire entry points (return immediately to the API; work runs on the
        // Hangfire server).
        Task BackfillTransactionsBgAsync(CancellationToken ct);
        Task BackfillExpensesBgAsync(CancellationToken ct);
    }
}
