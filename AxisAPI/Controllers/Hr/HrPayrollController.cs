using System.Text;
using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Hr
{
    /// <summary>Monthly payroll: pay components, employee overrides, advances, runs, approval/payment postings, payslips, CSV.</summary>
    [ApiController]
    [Route("api/hr/payroll")]
    [Authorize(Roles = "admin")]
    public class HrPayrollController : ControllerBase
    {
        private readonly IHrPayrollService _svc;
        public HrPayrollController(IHrPayrollService svc) => _svc = svc;

        private IActionResult From<T>(BaseResponse<T> r)
        {
            if (r.Success) return Ok(r);
            return r.Error == "not_found" ? NotFound(r) : BadRequest(r);
        }

        // ── Component types ─────────────────────────────────────────────
        [HttpGet("components")]
        public async Task<IActionResult> Components([FromQuery] bool includeInactive = true, CancellationToken ct = default)
            => Ok(await _svc.ListComponentTypesAsync(includeInactive, ct));

        [HttpPost("components")]
        public async Task<IActionResult> CreateComponent(HrPayComponentTypeUpsertDto dto, CancellationToken ct) => From(await _svc.CreateComponentTypeAsync(dto, ct));

        [HttpPut("components/{id:int}")]
        public async Task<IActionResult> UpdateComponent(int id, HrPayComponentTypeUpsertDto dto, CancellationToken ct) => From(await _svc.UpdateComponentTypeAsync(id, dto, ct));

        [HttpDelete("components/{id:int}")]
        public async Task<IActionResult> DeleteComponent(int id, CancellationToken ct) => From(await _svc.DeleteComponentTypeAsync(id, ct));

        // ── Employee overrides ──────────────────────────────────────────
        [HttpGet("employees/{employeeId:int}/components")]
        public async Task<IActionResult> EmployeeComponents(int employeeId, CancellationToken ct)
        {
            var r = await _svc.ListEmployeeComponentsAsync(employeeId, ct);
            return r is null ? NotFound() : Ok(r);
        }

        [HttpPut("employees/{employeeId:int}/components/{componentTypeId:int}")]
        public async Task<IActionResult> UpsertEmployeeComponent(int employeeId, int componentTypeId, HrEmployeePayComponentUpsertDto dto, CancellationToken ct)
            => From(await _svc.UpsertEmployeeComponentAsync(employeeId, componentTypeId, dto, ct));

        [HttpDelete("employees/{employeeId:int}/components/{componentTypeId:int}")]
        public async Task<IActionResult> RemoveEmployeeComponent(int employeeId, int componentTypeId, CancellationToken ct)
            => await _svc.RemoveEmployeeComponentAsync(employeeId, componentTypeId, ct) ? NoContent() : NotFound();

        // ── Advances ────────────────────────────────────────────────────
        [HttpGet("advances")]
        public async Task<IActionResult> Advances([FromQuery] int? year, [FromQuery] int? month, [FromQuery] int? employeeId, CancellationToken ct)
            => Ok(await _svc.ListAdvancesAsync(year, month, employeeId, ct));

        [HttpPost("advances")]
        public async Task<IActionResult> CreateAdvance(HrPayrollAdvanceUpsertDto dto, CancellationToken ct) => From(await _svc.CreateAdvanceAsync(dto, ct));

        [HttpPut("advances/{id:int}")]
        public async Task<IActionResult> UpdateAdvance(int id, HrPayrollAdvanceUpsertDto dto, CancellationToken ct) => From(await _svc.UpdateAdvanceAsync(id, dto, ct));

        [HttpDelete("advances/{id:int}")]
        public async Task<IActionResult> DeleteAdvance(int id, CancellationToken ct) => From(await _svc.DeleteAdvanceAsync(id, ct));

        // ── Runs ────────────────────────────────────────────────────────
        [HttpPost("runs/generate")]
        public async Task<IActionResult> Generate(HrPayrollGenerateDto dto, CancellationToken ct) => From(await _svc.GenerateAsync(dto, ct));

        [HttpGet("runs")]
        public async Task<IActionResult> Runs([FromQuery] int? year, CancellationToken ct) => Ok(await _svc.ListRunsAsync(year, ct));

        [HttpGet("runs/{id:int}")]
        public async Task<IActionResult> Run(int id, CancellationToken ct)
        {
            var r = await _svc.GetRunAsync(id, ct);
            return r is null ? NotFound() : Ok(r);
        }

        [HttpDelete("runs/{id:int}")]
        public async Task<IActionResult> DeleteRun(int id, CancellationToken ct) => From(await _svc.DeleteRunAsync(id, ct));

        [HttpPut("runs/{id:int}/lines/{lineId:int}")]
        public async Task<IActionResult> AdjustLine(int id, int lineId, HrPayrollLineAdjustDto dto, CancellationToken ct) => From(await _svc.AdjustLineAsync(id, lineId, dto, ct));

        /// <summary>Posts DR Salaries &amp; Wages Expense / CR Salaries Payable (+ CR Statutory Deductions Payable). Idempotent.</summary>
        [HttpPost("runs/{id:int}/approve")]
        public async Task<IActionResult> Approve(int id, CancellationToken ct) => From(await _svc.ApproveAsync(id, ct));

        /// <summary>Posts DR Salaries Payable / CR cash (default account 1000). Idempotent.</summary>
        [HttpPost("runs/{id:int}/mark-paid")]
        public async Task<IActionResult> MarkPaid(int id, [FromBody] HrPayrollMarkPaidDto? dto, CancellationToken ct)
            => From(await _svc.MarkPaidAsync(id, dto ?? new HrPayrollMarkPaidDto(), ct));

        // ── Outputs ─────────────────────────────────────────────────────
        [HttpGet("runs/{id:int}/payslips")]
        public async Task<IActionResult> Payslips(int id, CancellationToken ct)
        {
            var r = await _svc.PayslipsAsync(id, ct);
            return r is null ? NotFound() : Ok(r);
        }

        [HttpGet("runs/{id:int}/payslip/{employeeId:int}")]
        public async Task<IActionResult> Payslip(int id, int employeeId, CancellationToken ct)
        {
            var r = await _svc.PayslipAsync(id, employeeId, ct);
            return r is null ? NotFound() : Ok(r);
        }

        [HttpGet("runs/{id:int}/payslip/{employeeId:int}.html")]
        public async Task<IActionResult> PayslipHtml(int id, int employeeId, CancellationToken ct)
        {
            var r = await _svc.PayslipAsync(id, employeeId, ct);
            return r is null ? NotFound() : Content(_svc.PayslipHtml(r), "text/html", Encoding.UTF8);
        }

        /// <summary>CSV: employee,position,basePay,hoursWorked,overtimeHours,overtimePay,allowances,deductions,gross,net,excluded.</summary>
        [HttpGet("runs/{id:int}/export.csv")]
        public async Task<IActionResult> ExportCsv(int id, CancellationToken ct)
        {
            var csv = await _svc.ExportCsvAsync(id, ct);
            return csv is null ? NotFound() : File(Encoding.UTF8.GetBytes(csv), "text/csv", $"payroll_{id}.csv");
        }
    }
}
