namespace Application.DTOs
{
    // ── Employees ──────────────────────────────────────────────────────────
    public record HrEmployeeDto(
        int Id, int? UserId, string FullName, string? Phone, string? Email, string? Position, string? Department,
        decimal? HourlyRate, decimal? MonthlySalary, DateOnly HiredOn, DateOnly? TerminatedOn, bool IsActive,
        string? Notes, int? BranchId, bool HasPin);

    public record HrEmployeeCreateDto(
        string FullName, string? Phone = null, string? Email = null, string? Position = null, string? Department = null,
        decimal? HourlyRate = null, decimal? MonthlySalary = null, DateOnly? HiredOn = null, string? Notes = null,
        int? UserId = null, string? Pin = null);

    public record HrEmployeeUpdateDto(
        string? FullName = null, string? Phone = null, string? Email = null, string? Position = null, string? Department = null,
        decimal? HourlyRate = null, decimal? MonthlySalary = null, DateOnly? HiredOn = null, DateOnly? TerminatedOn = null,
        bool? IsActive = null, string? Notes = null);

    public record HrSetPinDto(string Pin);
    public record HrLinkUserDto(int? UserId);

    // ── Schedule ───────────────────────────────────────────────────────────
    public record HrShiftTemplateDto(int Id, string Name, TimeOnly StartTime, TimeOnly EndTime, string? Color);
    public record HrShiftTemplateUpsertDto(string Name, TimeOnly StartTime, TimeOnly EndTime, string? Color = null);

    public record HrShiftDto(
        int Id, int EmployeeId, string EmployeeName, DateOnly Date, DateTime StartOn, DateTime EndOn,
        int? TemplateId, string? Role, string? Notes, string Status, string CreatedBy);

    /// <summary>StartOn/EndOn may be omitted when TemplateId is given: the template times are applied to Date.</summary>
    public record HrShiftCreateDto(
        int EmployeeId, DateOnly Date, DateTime? StartOn = null, DateTime? EndOn = null, int? TemplateId = null,
        string? Role = null, string? Notes = null);

    public record HrShiftUpdateDto(
        int? EmployeeId = null, DateOnly? Date = null, DateTime? StartOn = null, DateTime? EndOn = null, int? TemplateId = null,
        string? Role = null, string? Notes = null, string? Status = null);

    public record HrWeekRowDto(int EmployeeId, string EmployeeName, string? Position, IReadOnlyList<HrShiftDto> Shifts);
    public record HrWeekViewDto(DateOnly WeekStart, IReadOnlyList<DateOnly> Days, IReadOnlyList<HrWeekRowDto> Rows);
    public record HrCopyWeekDto(DateOnly WeekStart);

    // ── Attendance ─────────────────────────────────────────────────────────
    public record HrClockRequestDto(string Pin, string? DeviceName = null);
    public record HrClockResultDto(
        bool Success, string Message, int? EmployeeId, string? EmployeeName, string? Action, DateTime? At, int? WorkedMinutes);

    public record HrAttendanceDto(
        int Id, int EmployeeId, string EmployeeName, DateTime ClockIn, DateTime? ClockOut, string Source,
        int? ScheduledShiftId, string? DeviceName, string? Notes, int? WorkedMinutes, int? BranchId);

    public record HrAttendanceManualDto(int EmployeeId, DateTime ClockIn, DateTime? ClockOut = null, string? Notes = null, int? ScheduledShiftId = null);
    public record HrAttendanceUpdateDto(DateTime? ClockIn = null, DateTime? ClockOut = null, string? Notes = null, int? ScheduledShiftId = null);

    public record HrDailySummaryRowDto(
        int EmployeeId, string EmployeeName, DateOnly Date, DateTime? ScheduledStart, DateTime? ScheduledEnd,
        DateTime? FirstClockIn, DateTime? LastClockOut, int WorkedMinutes, int LateMinutes, string Status);

    public record HrTimesheetRowDto(DateOnly Date, DateTime ClockIn, DateTime? ClockOut, int Minutes, decimal Amount);
    public record HrTimesheetDto(
        int EmployeeId, string EmployeeName, int Year, int Month, decimal? HourlyRate, int TotalMinutes, decimal TotalAmount,
        IReadOnlyList<HrTimesheetRowDto> Rows);

    // ── Leave ──────────────────────────────────────────────────────────────
    public record HrLeaveTypeDto(int Id, string Name, bool IsPaid, int DefaultDaysPerYear);
    public record HrLeaveTypeUpsertDto(string Name, bool IsPaid = true, int DefaultDaysPerYear = 0);

    public record HrLeaveRequestDto(
        int Id, int EmployeeId, string EmployeeName, int LeaveTypeId, string LeaveTypeName, DateOnly FromDate, DateOnly ToDate,
        int Days, string? Reason, string Status, DateTime RequestedOn, string? DecidedBy, DateTime? DecidedOn, string? DecisionNote);

    /// <summary>EmployeeId null = the caller's own employee record (via user link).</summary>
    public record HrLeaveRequestCreateDto(int LeaveTypeId, DateOnly FromDate, DateOnly ToDate, string? Reason = null, int? EmployeeId = null);
    public record HrLeaveDecisionDto(string? Note = null);

    public record HrLeaveBalanceItemDto(int LeaveTypeId, string LeaveTypeName, bool IsPaid, int DefaultDays, int UsedDays, int PendingDays, int Remaining);
    public record HrLeaveBalanceDto(int EmployeeId, string EmployeeName, int Year, IReadOnlyList<HrLeaveBalanceItemDto> Items);

    // ── Tips ───────────────────────────────────────────────────────────────
    public record HrTipParticipantDto(int EmployeeId, decimal? Points = null);
    public record HrTipAllocationDto(int Id, int EmployeeId, string EmployeeName, decimal? Points, decimal? Hours, decimal Amount);
    public record HrTipPoolDto(
        int Id, DateOnly Date, decimal TotalAmount, string Method, string Status, string? Notes, string CreatedBy,
        DateTime CreatedOn, DateTime? DistributedOn, int? BranchId, IReadOnlyList<HrTipAllocationDto> Allocations);

    public record HrTipPoolCreateDto(DateOnly Date, decimal TotalAmount, string Method, string? Notes = null, IReadOnlyList<HrTipParticipantDto>? Participants = null);
    public record HrTipPoolUpdateDto(DateOnly? Date = null, decimal? TotalAmount = null, string? Method = null, string? Notes = null, IReadOnlyList<HrTipParticipantDto>? Participants = null);

    public record HrTipReportEmployeeDto(int EmployeeId, string EmployeeName, decimal Amount);
    public record HrTipReportDayDto(DateOnly Date, int PoolCount, decimal Amount);
    public record HrTipReportDto(DateOnly From, DateOnly To, decimal Total, IReadOnlyList<HrTipReportDayDto> ByDay, IReadOnlyList<HrTipReportEmployeeDto> ByEmployee);
}
