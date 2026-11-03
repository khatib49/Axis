using Application.DTOs;

namespace Application.IServices
{
    public interface IOwnerSummaryService
    {
        /// <param name="day">Business day (UTC midnight); null → today.</param>
        Task<OwnerSummaryDto> BuildAsync(DateTime? day, CancellationToken ct = default);
        /// <param name="day">Business day (UTC midnight); null → today.</param>
        /// <param name="branchId">Null → all branches; otherwise only that branch's transactions.</param>
        Task<OwnerSummaryDto> BuildAsync(DateTime? day, int? branchId, CancellationToken ct = default);
    }
}
