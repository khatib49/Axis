using Application.DTOs;

namespace Application.IServices
{
    public interface IOwnerSummaryService
    {
        /// <param name="day">Business day (UTC midnight); null → today.</param>
        Task<OwnerSummaryDto> BuildAsync(DateTime? day, CancellationToken ct = default);
    }
}
