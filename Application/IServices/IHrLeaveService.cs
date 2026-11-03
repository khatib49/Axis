using Application.DTOs;

namespace Application.IServices
{
    public interface IHrLeaveService
    {
        Task<List<HrLeaveTypeDto>> ListTypesAsync(CancellationToken ct = default);
        Task<HrLeaveTypeDto> CreateTypeAsync(HrLeaveTypeUpsertDto dto, CancellationToken ct = default);
        Task<bool> UpdateTypeAsync(int id, HrLeaveTypeUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteTypeAsync(int id, CancellationToken ct = default);

        Task<List<HrLeaveRequestDto>> ListRequestsAsync(string? status, int? employeeId, int? year, CancellationToken ct = default);
        Task<List<HrLeaveRequestDto>> ListMyRequestsAsync(CancellationToken ct = default);
        Task<BaseResponse<HrLeaveRequestDto>> CreateRequestAsync(HrLeaveRequestCreateDto dto, bool isAdmin, CancellationToken ct = default);
        Task<BaseResponse<HrLeaveRequestDto>> DecideAsync(int id, bool approve, string? note, CancellationToken ct = default);
        Task<BaseResponse<HrLeaveRequestDto>> CancelAsync(int id, bool isAdmin, CancellationToken ct = default);
        Task<HrLeaveBalanceDto?> BalanceAsync(int employeeId, int year, CancellationToken ct = default);
        Task<List<HrLeaveBalanceDto>> AllBalancesAsync(int year, CancellationToken ct = default);
    }
}
