using Application.DTOs;

namespace Application.IServices
{
    public interface IShiftService
    {
        Task<BaseResponse<CashierShiftDto>> OpenAsync(OpenShiftDto dto, CancellationToken ct = default);
        /// <summary>The caller's open shift (optionally on a given register), or null.</summary>
        Task<CashierShiftDto?> GetCurrentAsync(string? registerName = null, CancellationToken ct = default);
        Task<BaseResponse<CashMovementDto>> PayInAsync(CashMovementCreateDto dto, int? shiftId = null, CancellationToken ct = default);
        Task<BaseResponse<CashMovementDto>> PayOutAsync(CashMovementCreateDto dto, int? shiftId = null, CancellationToken ct = default);
        Task<BaseResponse<ShiftReportDto>> XReportAsync(int? shiftId = null, CancellationToken ct = default);
        Task<BaseResponse<ShiftReportDto>> CloseAsync(CloseShiftDto dto, int? shiftId = null, CancellationToken ct = default);
        Task<PaginatedResponse<CashierShiftDto>> ListAsync(ShiftFilterDto filter, CancellationToken ct = default);
        Task<CashierShiftDto?> GetAsync(int id, CancellationToken ct = default);
        Task<ShiftReportDto?> ZReportAsync(int shiftId, CancellationToken ct = default);
    }
}
