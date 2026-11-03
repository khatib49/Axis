using Application.DTOs;

namespace Application.IServices
{
    public interface IHrAttendanceService
    {
        Task<HrClockResultDto> ClockAsync(HrClockRequestDto dto, CancellationToken ct = default);
        Task<BaseResponse<HrAttendanceDto>> AddManualAsync(HrAttendanceManualDto dto, CancellationToken ct = default);
        Task<BaseResponse<HrAttendanceDto>> UpdateAsync(int id, HrAttendanceUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<List<HrAttendanceDto>> ListAsync(DateOnly from, DateOnly to, int? employeeId, CancellationToken ct = default);
        Task<List<HrDailySummaryRowDto>> DailySummaryAsync(DateOnly date, CancellationToken ct = default);
        Task<HrTimesheetDto?> TimesheetAsync(int employeeId, int year, int month, CancellationToken ct = default);
        Task<string> ExportCsvAsync(DateOnly from, DateOnly to, int? employeeId, CancellationToken ct = default);
    }
}
