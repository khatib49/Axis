using Application.DTOs;

namespace Application.IServices
{
    public interface ITenderService
    {
        Task<BaseResponse<TenderSummaryDto>> AddAsync(AddTendersDto dto, CancellationToken ct = default);
        Task<BaseResponse<TenderSummaryDto>> SummaryAsync(int transactionId, CancellationToken ct = default);
        Task<List<PaymentTenderDto>> ListByShiftAsync(int shiftId, CancellationToken ct = default);
        /// <summary>Removes a tender entered by mistake (only while its shift is still open or it has no shift).</summary>
        Task<BaseResponse<TenderSummaryDto>> DeleteAsync(int tenderId, CancellationToken ct = default);
    }
}
