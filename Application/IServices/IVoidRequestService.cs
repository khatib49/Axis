using Application.DTOs;

namespace Application.IServices
{
    public interface IVoidRequestService
    {
        // Reasons
        Task<List<VoidReasonDto>> ListReasonsAsync(bool includeInactive = false, CancellationToken ct = default);
        Task<BaseResponse<VoidReasonDto>> CreateReasonAsync(VoidReasonCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<VoidReasonDto>> UpdateReasonAsync(int id, VoidReasonUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteReasonAsync(int id, CancellationToken ct = default);

        // Requests
        Task<BaseResponse<TillVoidRequestDto>> CreateAsync(VoidRequestCreateDto dto, CancellationToken ct = default);
        Task<PaginatedResponse<TillVoidRequestDto>> ListAsync(VoidRequestFilterDto filter, CancellationToken ct = default);
        Task<List<TillVoidRequestDto>> ListPendingAsync(CancellationToken ct = default);
        Task<TillVoidRequestDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<TillVoidRequestDto>> ApproveAsync(int id, VoidDecisionDto dto, CancellationToken ct = default);
        Task<BaseResponse<TillVoidRequestDto>> RejectAsync(int id, VoidDecisionDto dto, CancellationToken ct = default);
    }
}
