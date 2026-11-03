using Application.DTOs;

namespace Application.IServices
{
    public interface IHrPayrollService
    {
        // Component types (seeded lazily with NSSF employee 3%, Transport, Income tax placeholder)
        Task<List<HrPayComponentTypeDto>> ListComponentTypesAsync(bool includeInactive = true, CancellationToken ct = default);
        Task<BaseResponse<HrPayComponentTypeDto>> CreateComponentTypeAsync(HrPayComponentTypeUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<HrPayComponentTypeDto>> UpdateComponentTypeAsync(int id, HrPayComponentTypeUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteComponentTypeAsync(int id, CancellationToken ct = default);

        // Employee overrides
        Task<List<HrEmployeePayComponentDto>?> ListEmployeeComponentsAsync(int employeeId, CancellationToken ct = default);
        Task<BaseResponse<HrEmployeePayComponentDto>> UpsertEmployeeComponentAsync(int employeeId, int componentTypeId, HrEmployeePayComponentUpsertDto dto, CancellationToken ct = default);
        Task<bool> RemoveEmployeeComponentAsync(int employeeId, int componentTypeId, CancellationToken ct = default);

        // Advances
        Task<List<HrPayrollAdvanceDto>> ListAdvancesAsync(int? year, int? month, int? employeeId, CancellationToken ct = default);
        Task<BaseResponse<HrPayrollAdvanceDto>> CreateAdvanceAsync(HrPayrollAdvanceUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<HrPayrollAdvanceDto>> UpdateAdvanceAsync(int id, HrPayrollAdvanceUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteAdvanceAsync(int id, CancellationToken ct = default);

        // Runs
        Task<BaseResponse<HrPayrollRunDto>> GenerateAsync(HrPayrollGenerateDto dto, CancellationToken ct = default);
        Task<List<HrPayrollRunDto>> ListRunsAsync(int? year, CancellationToken ct = default);
        Task<HrPayrollRunDto?> GetRunAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteRunAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<HrPayrollLineDto>> AdjustLineAsync(int runId, int lineId, HrPayrollLineAdjustDto dto, CancellationToken ct = default);
        Task<BaseResponse<HrPayrollRunDto>> ApproveAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<HrPayrollRunDto>> MarkPaidAsync(int id, HrPayrollMarkPaidDto dto, CancellationToken ct = default);

        // Outputs
        Task<List<HrPayslipDto>?> PayslipsAsync(int runId, CancellationToken ct = default);
        Task<HrPayslipDto?> PayslipAsync(int runId, int employeeId, CancellationToken ct = default);
        string PayslipHtml(HrPayslipDto slip);
        Task<string?> ExportCsvAsync(int runId, CancellationToken ct = default);
    }
}
