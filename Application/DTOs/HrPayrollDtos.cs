namespace Application.DTOs
{
    // ── Pay component types ──────────────────────────────────────────────
    public record HrPayComponentTypeDto(
        int Id, string Name, string Kind, string Method, decimal Rate, bool IsStatutory, bool IsActive, bool DefaultForAll);

    public record HrPayComponentTypeUpsertDto(
        string Name, string Kind, string Method, decimal Rate, bool IsStatutory = false, bool IsActive = true, bool DefaultForAll = false);

    /// <summary>Effective component for one employee: the type plus the override (if any).</summary>
    public record HrEmployeePayComponentDto(
        int ComponentTypeId, string Name, string Kind, string Method, decimal DefaultRate, bool IsStatutory, bool DefaultForAll,
        bool HasOverride, decimal? Amount, decimal? Rate, bool? OverrideIsActive, bool Applies, decimal EffectiveRate);

    public record HrEmployeePayComponentUpsertDto(decimal? Amount = null, decimal? Rate = null, bool IsActive = true);

    // ── Advances ─────────────────────────────────────────────────────────
    public record HrPayrollAdvanceDto(
        int Id, int EmployeeId, string EmployeeName, decimal Amount, DateOnly On, int? RecoveredInRunId, string? Notes,
        string? CreatedBy, DateTime CreatedOn);

    public record HrPayrollAdvanceUpsertDto(int EmployeeId, decimal Amount, DateOnly On, string? Notes = null);

    // ── Runs ─────────────────────────────────────────────────────────────
    public record HrPayrollGenerateDto(int Year, int Month, string? Notes = null);

    public record HrPayrollComponentLineDto(
        string Name, string Kind, string Method, decimal Rate, decimal Amount, bool IsStatutory, string Source);

    public record HrPayrollLineDto(
        int Id, int RunId, int EmployeeId, string EmployeeName, string? Position,
        decimal BasePay, decimal HoursWorked, decimal? HourlyRate, decimal? MonthlySalary,
        decimal OvertimeHours, decimal OvertimePay, decimal Allowances, decimal Deductions, decimal StatutoryDeductions,
        decimal AdvancesRecovered, decimal ManualAllowance, decimal ManualDeduction, decimal Gross, decimal Net,
        IReadOnlyList<HrPayrollComponentLineDto> Components, string? Notes, bool IsExcluded);

    public record HrPayrollRunDto(
        int Id, int Year, int Month, string Status, DateTime GeneratedOn, string? GeneratedBy,
        DateTime? ApprovedOn, string? ApprovedBy, DateTime? PaidOn, string? PaidBy,
        decimal TotalGross, decimal TotalDeductions, decimal TotalStatutory, decimal TotalNet,
        int? JournalEntryId, int? PaymentJournalEntryId, string? Notes, int? BranchId,
        int EmployeeCount, int ExcludedCount, IReadOnlyList<HrPayrollLineDto>? Lines);

    public record HrPayrollLineAdjustDto(
        string? Notes = null, bool? IsExcluded = null, decimal? ManualAllowance = null, decimal? ManualDeduction = null);

    public record HrPayrollMarkPaidDto(string? CashAccountNumber = null, DateTime? PaidOn = null);

    public record HrPayslipDto(
        int RunId, int Year, int Month, string RunStatus, int EmployeeId, string EmployeeName, string? Position, string? Department,
        decimal BasePay, decimal HoursWorked, decimal? HourlyRate, decimal? MonthlySalary, decimal OvertimeHours, decimal OvertimePay,
        IReadOnlyList<HrPayrollComponentLineDto> Allowances, IReadOnlyList<HrPayrollComponentLineDto> Deductions,
        decimal TotalAllowances, decimal TotalDeductions, decimal Gross, decimal Net, string? Notes, bool IsExcluded);
}
