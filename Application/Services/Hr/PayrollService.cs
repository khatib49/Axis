using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Hr
{
    /// <summary>
    /// Monthly payroll: pay components (allowances/deductions), employee overrides, advances,
    /// run generation from attendance + salaries, manual adjustments, approval (accrual journal
    /// entry) and payment (cash journal entry). Period locks are enforced on approve / mark-paid.
    /// </summary>
    public class PayrollService : IHrPayrollService
    {
        public const string ReferenceTypeRun = "Payroll";
        public const string ReferenceTypePayment = "PayrollPayment";

        // Account resolution: first an active account at one of these numbers whose name matches the
        // key, then any active account of the type whose name matches, otherwise create one at the
        // first free number. The stock chart seeds 5400 "Salaries & Wages Expense" and 2200 "Salaries
        // Payable" (2300 there is "Loans Payable"), so those are matched before anything is created.
        private static readonly string[] ExpenseNumbers = { "5400", "5800" };
        private static readonly string[] PayableNumbers = { "2200", "2300", "2301" };
        private static readonly string[] StatutoryNumbers = { "2310", "2311" };
        public const string ExpenseAccountName = "Salaries & Wages Expense";
        public const string PayableAccountName = "Salaries Payable";
        public const string StatutoryAccountName = "Statutory Deductions Payable";
        public const string DefaultCashAccountNumber = "1000";

        private static readonly string[] Kinds = { PayComponentType.KindAllowance, PayComponentType.KindDeduction };
        private static readonly string[] Methods = { PayComponentType.MethodFixed, PayComponentType.MethodPercentOfGross };
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;
        private readonly IJournalService _journal;
        private readonly IAccountingPeriodService _periods;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<PayrollService> _log;
        private readonly Func<DateTime> _now;

        public PayrollService(ApplicationDbContext db, IJournalService journal, IAccountingPeriodService periods,
            ICurrentUserAccessor user, ILogger<PayrollService> log)
            : this(db, journal, periods, user, log, () => DateTime.UtcNow) { }

        /// <summary>Test seam: fixed clock.</summary>
        internal PayrollService(ApplicationDbContext db, IJournalService journal, IAccountingPeriodService periods,
            ICurrentUserAccessor user, ILogger<PayrollService> log, Func<DateTime> now)
        {
            _db = db; _journal = journal; _periods = periods; _user = user; _log = log; _now = now;
        }

        private static BaseResponse<T> Fail<T>(string code, string message) => new(false, code, message);
        private static BaseResponse<T> Ok<T>(string message, T data) => new(true, null, message, data);
        private static decimal M(decimal v) => HrPayrollCalculations.Money(v);

        // ══════════════════════════════════════════════════════════════════
        // Component types
        // ══════════════════════════════════════════════════════════════════
        private static HrPayComponentTypeDto ToDto(PayComponentType t)
            => new(t.Id, t.Name, t.Kind, t.Method, t.Rate, t.IsStatutory, t.IsActive, t.DefaultForAll);

        /// <summary>Seeds the default components the first time payroll is used.</summary>
        private async Task EnsureSeededAsync(CancellationToken ct)
        {
            if (await _db.Set<PayComponentType>().AnyAsync(ct)) return;
            _db.AddRange(
                new PayComponentType { Name = "NSSF employee", Kind = PayComponentType.KindDeduction, Method = PayComponentType.MethodPercentOfGross, Rate = 3m, IsStatutory = true, IsActive = true, DefaultForAll = true },
                new PayComponentType { Name = "Transport", Kind = PayComponentType.KindAllowance, Method = PayComponentType.MethodFixed, Rate = 0m, IsStatutory = false, IsActive = true, DefaultForAll = false },
                new PayComponentType { Name = "Income tax", Kind = PayComponentType.KindDeduction, Method = PayComponentType.MethodPercentOfGross, Rate = 0m, IsStatutory = true, IsActive = true, DefaultForAll = false });
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Payroll: seeded default pay components");
        }

        public async Task<List<HrPayComponentTypeDto>> ListComponentTypesAsync(bool includeInactive = true, CancellationToken ct = default)
        {
            await EnsureSeededAsync(ct);
            var q = _db.Set<PayComponentType>().AsNoTracking();
            if (!includeInactive) q = q.Where(t => t.IsActive);
            return (await q.OrderBy(t => t.Kind).ThenBy(t => t.Name).ToListAsync(ct)).Select(ToDto).ToList();
        }

        private static string? ValidateType(HrPayComponentTypeUpsertDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required";
            if (!Kinds.Contains(dto.Kind)) return "Kind must be Allowance or Deduction";
            if (!Methods.Contains(dto.Method)) return "Method must be Fixed or PercentOfGross";
            if (dto.Rate < 0) return "Rate must not be negative";
            if (dto.Method == PayComponentType.MethodPercentOfGross && dto.Rate > 100) return "Percentage must be 0-100";
            return null;
        }

        public async Task<BaseResponse<HrPayComponentTypeDto>> CreateComponentTypeAsync(HrPayComponentTypeUpsertDto dto, CancellationToken ct = default)
        {
            await EnsureSeededAsync(ct);
            var err = ValidateType(dto);
            if (err != null) return Fail<HrPayComponentTypeDto>("validation", err);
            var name = dto.Name.Trim();
            if (await _db.Set<PayComponentType>().AnyAsync(t => t.Name == name, ct)) return Fail<HrPayComponentTypeDto>("validation", "A component with that name already exists");
            var t = new PayComponentType { Name = name, Kind = dto.Kind, Method = dto.Method, Rate = dto.Rate, IsStatutory = dto.IsStatutory, IsActive = dto.IsActive, DefaultForAll = dto.DefaultForAll };
            _db.Add(t);
            await _db.SaveChangesAsync(ct);
            return Ok("Component created", ToDto(t));
        }

        public async Task<BaseResponse<HrPayComponentTypeDto>> UpdateComponentTypeAsync(int id, HrPayComponentTypeUpsertDto dto, CancellationToken ct = default)
        {
            var t = await _db.Set<PayComponentType>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Fail<HrPayComponentTypeDto>("not_found", "Component not found");
            var err = ValidateType(dto);
            if (err != null) return Fail<HrPayComponentTypeDto>("validation", err);
            var name = dto.Name.Trim();
            if (await _db.Set<PayComponentType>().AnyAsync(x => x.Name == name && x.Id != id, ct)) return Fail<HrPayComponentTypeDto>("validation", "A component with that name already exists");
            t.Name = name; t.Kind = dto.Kind; t.Method = dto.Method; t.Rate = dto.Rate;
            t.IsStatutory = dto.IsStatutory; t.IsActive = dto.IsActive; t.DefaultForAll = dto.DefaultForAll;
            await _db.SaveChangesAsync(ct);
            return Ok("Component updated", ToDto(t));
        }

        public async Task<BaseResponse<bool>> DeleteComponentTypeAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<PayComponentType>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Fail<bool>("not_found", "Component not found");
            if (await _db.Set<EmployeePayComponent>().AnyAsync(o => o.ComponentTypeId == id, ct))
            {
                // Keep history intact: components that employees override are deactivated instead.
                t.IsActive = false;
                await _db.SaveChangesAsync(ct);
                return Ok("Component has employee overrides and was deactivated instead of deleted", false);
            }
            _db.Remove(t);
            await _db.SaveChangesAsync(ct);
            return Ok("Component deleted", true);
        }

        // ══════════════════════════════════════════════════════════════════
        // Employee overrides
        // ══════════════════════════════════════════════════════════════════
        private static HrEmployeePayComponentDto ToDto(PayComponentType t, EmployeePayComponent? o)
        {
            var applies = o != null ? o.IsActive : t.DefaultForAll;
            var effective = t.Method == PayComponentType.MethodFixed ? (o?.Amount ?? t.Rate) : (o?.Rate ?? t.Rate);
            return new HrEmployeePayComponentDto(t.Id, t.Name, t.Kind, t.Method, t.Rate, t.IsStatutory, t.DefaultForAll,
                o != null, o?.Amount, o?.Rate, o?.IsActive, applies, effective);
        }

        public async Task<List<HrEmployeePayComponentDto>?> ListEmployeeComponentsAsync(int employeeId, CancellationToken ct = default)
        {
            if (!await _db.Set<Employee>().AnyAsync(e => e.Id == employeeId, ct)) return null;
            await EnsureSeededAsync(ct);
            var types = await _db.Set<PayComponentType>().AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Kind).ThenBy(t => t.Name).ToListAsync(ct);
            var overrides = await _db.Set<EmployeePayComponent>().AsNoTracking().Where(o => o.EmployeeId == employeeId).ToListAsync(ct);
            return types.Select(t => ToDto(t, overrides.FirstOrDefault(o => o.ComponentTypeId == t.Id))).ToList();
        }

        public async Task<BaseResponse<HrEmployeePayComponentDto>> UpsertEmployeeComponentAsync(int employeeId, int componentTypeId, HrEmployeePayComponentUpsertDto dto, CancellationToken ct = default)
        {
            if (!await _db.Set<Employee>().AnyAsync(e => e.Id == employeeId, ct)) return Fail<HrEmployeePayComponentDto>("not_found", "Employee not found");
            var t = await _db.Set<PayComponentType>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == componentTypeId, ct);
            if (t is null) return Fail<HrEmployeePayComponentDto>("not_found", "Component not found");
            if (dto.Amount is < 0) return Fail<HrEmployeePayComponentDto>("validation", "Amount must not be negative");
            if (dto.Rate is < 0 or > 100) return Fail<HrEmployeePayComponentDto>("validation", "Rate must be 0-100");
            var o = await _db.Set<EmployeePayComponent>().FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.ComponentTypeId == componentTypeId, ct);
            if (o is null)
            {
                o = new EmployeePayComponent { EmployeeId = employeeId, ComponentTypeId = componentTypeId };
                _db.Add(o);
            }
            else o.UpdatedOn = _now();
            o.Amount = dto.Amount; o.Rate = dto.Rate; o.IsActive = dto.IsActive;
            await _db.SaveChangesAsync(ct);
            return Ok("Saved", ToDto(t, o));
        }

        public async Task<bool> RemoveEmployeeComponentAsync(int employeeId, int componentTypeId, CancellationToken ct = default)
        {
            var o = await _db.Set<EmployeePayComponent>().FirstOrDefaultAsync(x => x.EmployeeId == employeeId && x.ComponentTypeId == componentTypeId, ct);
            if (o is null) return false;
            _db.Remove(o);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // ══════════════════════════════════════════════════════════════════
        // Advances
        // ══════════════════════════════════════════════════════════════════
        private static HrPayrollAdvanceDto ToDto(PayrollAdvance a, string name)
            => new(a.Id, a.EmployeeId, name, a.Amount, a.On, a.RecoveredInRunId, a.Notes, a.CreatedBy, a.CreatedOn);

        private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            var set = ids.Distinct().ToList();
            return await _db.Set<Employee>().AsNoTracking().Where(e => set.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        }

        public async Task<List<HrPayrollAdvanceDto>> ListAdvancesAsync(int? year, int? month, int? employeeId, CancellationToken ct = default)
        {
            var q = _db.Set<PayrollAdvance>().AsNoTracking();
            if (year.HasValue && month.HasValue)
            {
                var from = new DateOnly(year.Value, month.Value, 1);
                var to = from.AddMonths(1);
                q = q.Where(a => a.On >= from && a.On < to);
            }
            else if (year.HasValue) q = q.Where(a => a.On.Year == year.Value);
            if (employeeId.HasValue) q = q.Where(a => a.EmployeeId == employeeId);
            var rows = await q.OrderByDescending(a => a.On).ThenByDescending(a => a.Id).ToListAsync(ct);
            var names = await NamesAsync(rows.Select(r => r.EmployeeId), ct);
            return rows.Select(r => ToDto(r, names.GetValueOrDefault(r.EmployeeId, ""))).ToList();
        }

        public async Task<BaseResponse<HrPayrollAdvanceDto>> CreateAdvanceAsync(HrPayrollAdvanceUpsertDto dto, CancellationToken ct = default)
        {
            var emp = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
            if (emp is null) return Fail<HrPayrollAdvanceDto>("validation", "Employee not found");
            if (dto.Amount <= 0) return Fail<HrPayrollAdvanceDto>("validation", "Amount must be positive");
            var closed = await _db.Set<PayrollRun>().AsNoTracking().AnyAsync(r => r.Year == dto.On.Year && r.Month == dto.On.Month && r.Status != PayrollRun.StatusDraft, ct);
            if (closed) return Fail<HrPayrollAdvanceDto>("validation", $"Payroll for {dto.On:yyyy-MM} is already approved; date the advance in an open month");
            var a = new PayrollAdvance { EmployeeId = emp.Id, Amount = M(dto.Amount), On = dto.On, Notes = dto.Notes, CreatedBy = _user.UserName, CreatedOn = _now(), BranchId = _user.BranchId };
            _db.Add(a);
            await _db.SaveChangesAsync(ct);
            return Ok("Advance recorded", ToDto(a, emp.FullName));
        }

        public async Task<BaseResponse<HrPayrollAdvanceDto>> UpdateAdvanceAsync(int id, HrPayrollAdvanceUpsertDto dto, CancellationToken ct = default)
        {
            var a = await _db.Set<PayrollAdvance>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Fail<HrPayrollAdvanceDto>("not_found", "Advance not found");
            if (a.RecoveredInRunId.HasValue) return Fail<HrPayrollAdvanceDto>("validation", "Advance was already recovered by a payroll run");
            var emp = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
            if (emp is null) return Fail<HrPayrollAdvanceDto>("validation", "Employee not found");
            if (dto.Amount <= 0) return Fail<HrPayrollAdvanceDto>("validation", "Amount must be positive");
            a.EmployeeId = emp.Id; a.Amount = M(dto.Amount); a.On = dto.On; a.Notes = dto.Notes;
            await _db.SaveChangesAsync(ct);
            return Ok("Advance updated", ToDto(a, emp.FullName));
        }

        public async Task<BaseResponse<bool>> DeleteAdvanceAsync(int id, CancellationToken ct = default)
        {
            var a = await _db.Set<PayrollAdvance>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (a is null) return Fail<bool>("not_found", "Advance not found");
            if (a.RecoveredInRunId.HasValue) return Fail<bool>("validation", "Advance was already recovered by a payroll run");
            _db.Remove(a);
            await _db.SaveChangesAsync(ct);
            return Ok("Advance deleted", true);
        }

        // ══════════════════════════════════════════════════════════════════
        // Runs
        // ══════════════════════════════════════════════════════════════════
        private static IReadOnlyList<HrPayrollComponentLineDto> Components(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<HrPayrollComponentLineDto>();
            try { return JsonSerializer.Deserialize<List<HrPayrollComponentLineDto>>(json, JsonOpts) ?? new List<HrPayrollComponentLineDto>(); }
            catch { return Array.Empty<HrPayrollComponentLineDto>(); }
        }

        private static HrPayrollLineDto ToDto(PayrollLine l) => new(
            l.Id, l.RunId, l.EmployeeId, l.EmployeeName, l.Position, l.BasePay, l.HoursWorked, l.HourlyRate, l.MonthlySalary,
            l.OvertimeHours, l.OvertimePay, l.Allowances, l.Deductions, l.StatutoryDeductions, l.AdvancesRecovered,
            l.ManualAllowance, l.ManualDeduction, l.Gross, l.Net, Components(l.ComponentsJson), l.Notes, l.IsExcluded);

        private static HrPayrollRunDto ToDto(PayrollRun r, bool withLines)
        {
            var lines = r.Lines.OrderBy(l => l.EmployeeName).ThenBy(l => l.EmployeeId).ToList();
            return new HrPayrollRunDto(
                r.Id, r.Year, r.Month, r.Status, r.GeneratedOn, r.GeneratedBy, r.ApprovedOn, r.ApprovedBy, r.PaidOn, r.PaidBy,
                r.TotalGross, r.TotalDeductions, r.TotalStatutory, r.TotalNet, r.JournalEntryId, r.PaymentJournalEntryId, r.Notes, r.BranchId,
                lines.Count(l => !l.IsExcluded), lines.Count(l => l.IsExcluded), withLines ? lines.Select(ToDto).ToList() : null);
        }

        private IQueryable<PayrollRun> Runs() => _db.Set<PayrollRun>().Include(r => r.Lines);

        private static (DateTime Start, DateTime End) MonthWindow(int year, int month)
        {
            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            return (start, start.AddMonths(1));
        }

        /// <summary>Everything needed to compute lines for one month, loaded once per run.</summary>
        private sealed class RunContext
        {
            public List<PayComponentType> Types = new();
            public List<EmployeePayComponent> Overrides = new();
            public Dictionary<int, int> MinutesByEmployee = new();
            public List<PayrollAdvance> Advances = new();
        }

        private async Task<RunContext> LoadContextAsync(int year, int month, int runId, CancellationToken ct)
        {
            await EnsureSeededAsync(ct);
            var (start, end) = MonthWindow(year, month);
            var from = DateOnly.FromDateTime(start);
            var to = DateOnly.FromDateTime(end);
            var ctx = new RunContext
            {
                Types = await _db.Set<PayComponentType>().AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.Kind).ThenBy(t => t.Name).ToListAsync(ct),
                Overrides = await _db.Set<EmployeePayComponent>().AsNoTracking().ToListAsync(ct),
                // Closed attendance records that started inside the month.
                MinutesByEmployee = (await _db.Set<AttendanceRecord>().AsNoTracking()
                        .Where(r => r.ClockIn >= start && r.ClockIn < end && r.ClockOut != null)
                        .Select(r => new { r.EmployeeId, Minutes = r.WorkedMinutes ?? 0 }).ToListAsync(ct))
                    .GroupBy(x => x.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(x => x.Minutes)),
                // Advances given in the month that no other run has recovered yet.
                Advances = await _db.Set<PayrollAdvance>()
                    .Where(a => a.On >= from && a.On < to && (a.RecoveredInRunId == null || a.RecoveredInRunId == runId)).ToListAsync(ct),
            };
            return ctx;
        }

        /// <summary>Computes every figure of a line from the employee, attendance, components, advances and manual adjustments.</summary>
        private static void ComputeLine(PayrollLine line, Employee emp, RunContext ctx)
        {
            var minutes = ctx.MinutesByEmployee.GetValueOrDefault(emp.Id, 0);
            var bp = HrPayrollCalculations.ComputeBase(emp.MonthlySalary, emp.HourlyRate, minutes);
            line.EmployeeName = emp.FullName; line.Position = emp.Position;
            line.HourlyRate = emp.HourlyRate; line.MonthlySalary = emp.MonthlySalary;
            line.BasePay = bp.Base; line.HoursWorked = bp.Hours; line.OvertimeHours = bp.OvertimeHours; line.OvertimePay = bp.OvertimePay;

            var comps = new List<HrPayrollComponentLineDto>();
            var basis = M(bp.Base + bp.OvertimePay);
            decimal allowances = 0, deductions = 0, statutory = 0, advances = 0;

            foreach (var t in ctx.Types)
            {
                var o = ctx.Overrides.FirstOrDefault(x => x.EmployeeId == emp.Id && x.ComponentTypeId == t.Id);
                var applies = o != null ? o.IsActive : t.DefaultForAll;
                if (!applies) continue;
                var rate = t.Method == PayComponentType.MethodFixed ? (o?.Amount ?? t.Rate) : (o?.Rate ?? t.Rate);
                if (t.Kind != PayComponentType.KindAllowance) continue;
                var amount = HrPayrollCalculations.ComponentAmount(t.Method, rate, basis);
                allowances += amount;
                comps.Add(new HrPayrollComponentLineDto(t.Name, t.Kind, t.Method, rate, amount, t.IsStatutory, o != null ? "Override" : "Default"));
            }
            if (line.ManualAllowance != 0)
            {
                allowances += line.ManualAllowance;
                comps.Add(new HrPayrollComponentLineDto("Manual allowance", PayComponentType.KindAllowance, PayComponentType.MethodFixed, line.ManualAllowance, line.ManualAllowance, false, "Manual"));
            }
            var gross = M(bp.Base + bp.OvertimePay + allowances);

            foreach (var t in ctx.Types)
            {
                if (t.Kind != PayComponentType.KindDeduction) continue;
                var o = ctx.Overrides.FirstOrDefault(x => x.EmployeeId == emp.Id && x.ComponentTypeId == t.Id);
                var applies = o != null ? o.IsActive : t.DefaultForAll;
                if (!applies) continue;
                var rate = t.Method == PayComponentType.MethodFixed ? (o?.Amount ?? t.Rate) : (o?.Rate ?? t.Rate);
                var amount = HrPayrollCalculations.ComponentAmount(t.Method, rate, gross);
                deductions += amount;
                if (t.IsStatutory) statutory += amount;
                comps.Add(new HrPayrollComponentLineDto(t.Name, t.Kind, t.Method, rate, amount, t.IsStatutory, o != null ? "Override" : "Default"));
            }
            foreach (var a in ctx.Advances.Where(a => a.EmployeeId == emp.Id).OrderBy(a => a.On).ThenBy(a => a.Id))
            {
                advances += a.Amount;
                comps.Add(new HrPayrollComponentLineDto($"Advance {a.On:yyyy-MM-dd}", PayComponentType.KindDeduction, PayComponentType.MethodFixed, a.Amount, a.Amount, false, "Advance"));
            }
            if (line.ManualDeduction != 0)
            {
                deductions += line.ManualDeduction;
                comps.Add(new HrPayrollComponentLineDto("Manual deduction", PayComponentType.KindDeduction, PayComponentType.MethodFixed, line.ManualDeduction, line.ManualDeduction, false, "Manual"));
            }

            line.Allowances = M(allowances);
            line.AdvancesRecovered = M(advances);
            line.StatutoryDeductions = M(statutory);
            line.Deductions = M(deductions + advances);
            line.Gross = gross;
            line.Net = M(gross - line.Deductions);
            line.ComponentsJson = JsonSerializer.Serialize(comps, JsonOpts);
            if (!bp.IsSalaried && (emp.HourlyRate ?? 0) <= 0 && string.IsNullOrEmpty(line.Notes))
                line.Notes = "No monthly salary or hourly rate configured";
        }

        private static void Total(PayrollRun run)
        {
            var inc = run.Lines.Where(l => !l.IsExcluded).ToList();
            run.TotalGross = M(inc.Sum(l => l.Gross));
            run.TotalDeductions = M(inc.Sum(l => l.Deductions));
            run.TotalStatutory = M(inc.Sum(l => l.StatutoryDeductions));
            run.TotalNet = M(inc.Sum(l => l.Net));
        }

        public async Task<BaseResponse<HrPayrollRunDto>> GenerateAsync(HrPayrollGenerateDto dto, CancellationToken ct = default)
        {
            if (dto.Month < 1 || dto.Month > 12 || dto.Year < 2000 || dto.Year > 2100) return Fail<HrPayrollRunDto>("validation", "Invalid year/month");
            var run = await Runs().FirstOrDefaultAsync(r => r.Year == dto.Year && r.Month == dto.Month, ct);
            if (run != null && run.Status != PayrollRun.StatusDraft)
                return Fail<HrPayrollRunDto>("validation", $"Payroll {dto.Year}-{dto.Month:00} is already {run.Status}; it cannot be regenerated");

            var (start, end) = MonthWindow(dto.Year, dto.Month);
            var monthStart = DateOnly.FromDateTime(start);
            var monthEnd = DateOnly.FromDateTime(end).AddDays(-1);
            var employees = await _db.Set<Employee>().AsNoTracking()
                .Where(e => e.IsActive && e.HiredOn <= monthEnd && (e.TerminatedOn == null || e.TerminatedOn >= monthStart))
                .OrderBy(e => e.FullName).ToListAsync(ct);

            // Regeneration keeps manual exclusions and adjustments per employee and re-releases advances.
            var kept = new Dictionary<int, (bool Excluded, string? Notes, decimal ManualAllowance, decimal ManualDeduction)>();
            if (run is null)
            {
                run = new PayrollRun { Year = dto.Year, Month = dto.Month, BranchId = _user.BranchId };
                _db.Add(run);
            }
            else
            {
                foreach (var l in run.Lines) kept[l.EmployeeId] = (l.IsExcluded, l.Notes, l.ManualAllowance, l.ManualDeduction);
                _db.RemoveRange(run.Lines);
                run.Lines.Clear();
                var mine = await _db.Set<PayrollAdvance>().Where(a => a.RecoveredInRunId == run.Id).ToListAsync(ct);
                foreach (var a in mine) a.RecoveredInRunId = null;
            }
            run.Status = PayrollRun.StatusDraft;
            run.GeneratedOn = _now();
            run.GeneratedBy = _user.UserName;
            if (dto.Notes != null) run.Notes = dto.Notes;
            await _db.SaveChangesAsync(ct); // gives the run an id for advance recovery

            var ctx = await LoadContextAsync(dto.Year, dto.Month, run.Id, ct);
            foreach (var emp in employees)
            {
                var line = new PayrollLine { RunId = run.Id, EmployeeId = emp.Id };
                if (kept.TryGetValue(emp.Id, out var k))
                {
                    line.IsExcluded = k.Excluded; line.Notes = k.Notes; line.ManualAllowance = k.ManualAllowance; line.ManualDeduction = k.ManualDeduction;
                }
                ComputeLine(line, emp, ctx);
                run.Lines.Add(line);
                foreach (var a in ctx.Advances.Where(a => a.EmployeeId == emp.Id)) a.RecoveredInRunId = run.Id;
            }
            Total(run);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Payroll {Year}-{Month} generated: {Count} lines, gross {Gross}, net {Net}", run.Year, run.Month, run.Lines.Count, run.TotalGross, run.TotalNet);
            return Ok($"Payroll {run.Year}-{run.Month:00} generated for {run.Lines.Count} employee(s)", ToDto(run, true));
        }

        public async Task<List<HrPayrollRunDto>> ListRunsAsync(int? year, CancellationToken ct = default)
        {
            var q = Runs().AsNoTracking();
            if (year.HasValue) q = q.Where(r => r.Year == year);
            var rows = await q.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ToListAsync(ct);
            return rows.Select(r => ToDto(r, false)).ToList();
        }

        public async Task<HrPayrollRunDto?> GetRunAsync(int id, CancellationToken ct = default)
        {
            var run = await Runs().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
            return run is null ? null : ToDto(run, true);
        }

        public async Task<BaseResponse<bool>> DeleteRunAsync(int id, CancellationToken ct = default)
        {
            var run = await Runs().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run is null) return Fail<bool>("not_found", "Run not found");
            if (run.Status != PayrollRun.StatusDraft) return Fail<bool>("validation", "Only draft runs can be deleted");
            var mine = await _db.Set<PayrollAdvance>().Where(a => a.RecoveredInRunId == run.Id).ToListAsync(ct);
            foreach (var a in mine) a.RecoveredInRunId = null;
            _db.RemoveRange(run.Lines);
            _db.Remove(run);
            await _db.SaveChangesAsync(ct);
            return Ok("Run deleted", true);
        }

        public async Task<BaseResponse<HrPayrollLineDto>> AdjustLineAsync(int runId, int lineId, HrPayrollLineAdjustDto dto, CancellationToken ct = default)
        {
            var run = await Runs().FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run is null) return Fail<HrPayrollLineDto>("not_found", "Run not found");
            if (run.Status != PayrollRun.StatusDraft) return Fail<HrPayrollLineDto>("validation", $"Run is {run.Status}; adjustments are only allowed on drafts");
            var line = run.Lines.FirstOrDefault(l => l.Id == lineId);
            if (line is null) return Fail<HrPayrollLineDto>("not_found", "Line not found");
            if (dto.ManualAllowance is < 0 || dto.ManualDeduction is < 0) return Fail<HrPayrollLineDto>("validation", "Manual amounts must not be negative");
            var emp = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == line.EmployeeId, ct);
            if (emp is null) return Fail<HrPayrollLineDto>("not_found", "Employee not found");

            if (dto.Notes != null) line.Notes = dto.Notes;
            if (dto.IsExcluded.HasValue) line.IsExcluded = dto.IsExcluded.Value;
            if (dto.ManualAllowance.HasValue) line.ManualAllowance = M(dto.ManualAllowance.Value);
            if (dto.ManualDeduction.HasValue) line.ManualDeduction = M(dto.ManualDeduction.Value);

            var ctx = await LoadContextAsync(run.Year, run.Month, run.Id, ct);
            ComputeLine(line, emp, ctx);
            Total(run);
            await _db.SaveChangesAsync(ct);
            return Ok("Line updated", ToDto(line));
        }

        // ── Accounting ────────────────────────────────────────────────────
        private async Task<BaseResponse<Account>> ResolveAccountAsync(string typeName, string[] numbers, string nameKey, string createName, CancellationToken ct)
        {
            var all = await _db.Set<Account>().ToListAsync(ct);
            bool NameMatches(Account a) => a.AccountName.Contains(nameKey, StringComparison.OrdinalIgnoreCase);
            foreach (var n in numbers)
            {
                var hit = all.FirstOrDefault(a => a.IsActive && a.AccountNumber == n && NameMatches(a));
                if (hit != null) return Ok("", hit);
            }
            var type = await _db.Set<AccountType>().AsNoTracking().FirstOrDefaultAsync(t => t.TypeName == typeName, ct);
            if (type != null)
            {
                var byName = all.Where(a => a.IsActive && a.AccountTypeId == type.Id && NameMatches(a)).OrderBy(a => a.AccountNumber).FirstOrDefault();
                if (byName != null) return Ok("", byName);
            }
            if (type is null) return Fail<Account>("accounting", $"Account type '{typeName}' not found; cannot create {createName}");
            var free = numbers.FirstOrDefault(n => all.All(a => a.AccountNumber != n));
            if (free is null) return Fail<Account>("accounting", $"No free account number for {createName} (tried {string.Join(", ", numbers)})");
            var acc = new Account
            {
                AccountNumber = free, AccountName = createName, AccountTypeId = type.Id, Description = "Created by payroll",
                IsActive = true, AllowManualEntry = true, IsSystemAccount = false, CreatedBy = _user.UserId, CreatedAt = _now(),
            };
            _db.Add(acc);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Payroll created GL account {Number} {Name}", acc.AccountNumber, acc.AccountName);
            return Ok("", acc);
        }

        private DateTime PeriodEntryDate(PayrollRun run)
        {
            var (_, end) = MonthWindow(run.Year, run.Month);
            var last = end.AddDays(-1);
            var now = _now();
            return last > now ? now : last;
        }

        private async Task<int?> ExistingEntryIdAsync(string referenceType, int runId, CancellationToken ct)
        {
            var existing = await _journal.GetJournalEntriesByReferenceAsync(referenceType, runId, ct);
            return existing.Success ? existing.Data?.FirstOrDefault(e => !e.IsVoided)?.Id : null;
        }

        public async Task<BaseResponse<HrPayrollRunDto>> ApproveAsync(int id, CancellationToken ct = default)
        {
            var run = await Runs().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run is null) return Fail<HrPayrollRunDto>("not_found", "Run not found");
            if (run.Status != PayrollRun.StatusDraft) return Ok($"Run is already {run.Status}", ToDto(run, true)); // idempotent
            Total(run);
            if (run.Lines.All(l => l.IsExcluded)) return Fail<HrPayrollRunDto>("validation", "No employees included in this run");
            if (run.TotalGross <= 0) return Fail<HrPayrollRunDto>("validation", "Run gross total is zero; nothing to approve");

            var entryDate = PeriodEntryDate(run);
            var open = await _periods.EnsureOpenAsync(entryDate, ct);
            if (!open.Success) return Fail<HrPayrollRunDto>("period_locked", open.Error ?? "Accounting period is locked");

            var entryId = run.JournalEntryId ?? await ExistingEntryIdAsync(ReferenceTypeRun, run.Id, ct);
            if (entryId is null)
            {
                var expense = await ResolveAccountAsync("Expense", ExpenseNumbers, "salar", ExpenseAccountName, ct);
                if (!expense.Success) return Fail<HrPayrollRunDto>(expense.Error!, expense.Message);
                var payable = await ResolveAccountAsync("Liability", PayableNumbers, "salaries payable", PayableAccountName, ct);
                if (!payable.Success) return Fail<HrPayrollRunDto>(payable.Error!, payable.Message);
                var label = $"Payroll {run.Year}-{run.Month:00}";
                var lines = new List<JournalEntryLineCreateDto> { new(expense.Data!.Id, run.TotalGross, 0, $"{label} gross salaries & wages") };
                if (run.TotalStatutory > 0)
                {
                    var statutory = await ResolveAccountAsync("Liability", StatutoryNumbers, "statutory", StatutoryAccountName, ct);
                    if (!statutory.Success) return Fail<HrPayrollRunDto>(statutory.Error!, statutory.Message);
                    lines.Add(new(statutory.Data!.Id, 0, run.TotalStatutory, $"{label} statutory deductions withheld"));
                }
                var toPayable = M(run.TotalGross - run.TotalStatutory);
                if (toPayable > 0) lines.Add(new(payable.Data!.Id, 0, toPayable, $"{label} salaries payable"));

                var je = await _journal.CreateJournalEntryAsync(new JournalEntryCreateDto(entryDate, $"{label} accrual", ReferenceTypeRun, run.Id, lines), _user.UserId, ct);
                if (!je.Success || je.Data is null) return Fail<HrPayrollRunDto>("accounting", $"Journal entry failed: {je.Error}");
                var posted = await _journal.PostJournalEntryAsync(je.Data.Id, _user.UserId, ct);
                if (!posted.Success) return Fail<HrPayrollRunDto>("accounting", $"Journal posting failed: {posted.Error}");
                entryId = je.Data.Id;
            }

            run.JournalEntryId = entryId;
            run.Status = PayrollRun.StatusApproved;
            run.ApprovedOn = _now();
            run.ApprovedBy = _user.UserName;
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Payroll {Year}-{Month} approved by {User}; JE {Je}", run.Year, run.Month, run.ApprovedBy, entryId);
            return Ok("Payroll approved and posted", ToDto(run, true));
        }

        public async Task<BaseResponse<HrPayrollRunDto>> MarkPaidAsync(int id, HrPayrollMarkPaidDto dto, CancellationToken ct = default)
        {
            var run = await Runs().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (run is null) return Fail<HrPayrollRunDto>("not_found", "Run not found");
            if (run.Status == PayrollRun.StatusPaid) return Ok("Run is already Paid", ToDto(run, true)); // idempotent
            if (run.Status != PayrollRun.StatusApproved) return Fail<HrPayrollRunDto>("validation", "Approve the run before marking it paid");

            var paidOn = dto.PaidOn.HasValue ? DateTime.SpecifyKind(dto.PaidOn.Value, DateTimeKind.Utc) : _now();
            var open = await _periods.EnsureOpenAsync(paidOn, ct);
            if (!open.Success) return Fail<HrPayrollRunDto>("period_locked", open.Error ?? "Accounting period is locked");

            var entryId = run.PaymentJournalEntryId ?? await ExistingEntryIdAsync(ReferenceTypePayment, run.Id, ct);
            if (entryId is null && run.TotalNet > 0)
            {
                var cashNo = string.IsNullOrWhiteSpace(dto.CashAccountNumber) ? DefaultCashAccountNumber : dto.CashAccountNumber.Trim();
                var cash = await _db.Set<Account>().AsNoTracking().FirstOrDefaultAsync(a => a.AccountNumber == cashNo && a.IsActive, ct);
                if (cash is null) return Fail<HrPayrollRunDto>("validation", $"Cash/bank account {cashNo} not found or inactive");
                var payable = await ResolveAccountAsync("Liability", PayableNumbers, "salaries payable", PayableAccountName, ct);
                if (!payable.Success) return Fail<HrPayrollRunDto>(payable.Error!, payable.Message);
                var label = $"Payroll {run.Year}-{run.Month:00}";
                var je = await _journal.CreateJournalEntryAsync(new JournalEntryCreateDto(paidOn, $"{label} salaries paid", ReferenceTypePayment, run.Id,
                    new List<JournalEntryLineCreateDto>
                    {
                        new(payable.Data!.Id, run.TotalNet, 0, $"{label} net pay settled"),
                        new(cash.Id, 0, run.TotalNet, $"{label} net pay from {cash.AccountNumber} {cash.AccountName}"),
                    }), _user.UserId, ct);
                if (!je.Success || je.Data is null) return Fail<HrPayrollRunDto>("accounting", $"Journal entry failed: {je.Error}");
                var posted = await _journal.PostJournalEntryAsync(je.Data.Id, _user.UserId, ct);
                if (!posted.Success) return Fail<HrPayrollRunDto>("accounting", $"Journal posting failed: {posted.Error}");
                entryId = je.Data.Id;
            }

            run.PaymentJournalEntryId = entryId;
            run.Status = PayrollRun.StatusPaid;
            run.PaidOn = paidOn;
            run.PaidBy = _user.UserName;
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Payroll {Year}-{Month} marked paid by {User}; JE {Je}", run.Year, run.Month, run.PaidBy, entryId);
            return Ok("Payroll marked as paid", ToDto(run, true));
        }

        // ══════════════════════════════════════════════════════════════════
        // Outputs
        // ══════════════════════════════════════════════════════════════════
        private static HrPayslipDto ToSlip(PayrollRun run, PayrollLine l, Employee? emp)
        {
            var comps = Components(l.ComponentsJson);
            return new HrPayslipDto(run.Id, run.Year, run.Month, run.Status, l.EmployeeId, l.EmployeeName, l.Position, emp?.Department,
                l.BasePay, l.HoursWorked, l.HourlyRate, l.MonthlySalary, l.OvertimeHours, l.OvertimePay,
                comps.Where(c => c.Kind == PayComponentType.KindAllowance).ToList(),
                comps.Where(c => c.Kind == PayComponentType.KindDeduction).ToList(),
                l.Allowances, l.Deductions, l.Gross, l.Net, l.Notes, l.IsExcluded);
        }

        public async Task<List<HrPayslipDto>?> PayslipsAsync(int runId, CancellationToken ct = default)
        {
            var run = await Runs().AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run is null) return null;
            var ids = run.Lines.Select(l => l.EmployeeId).ToList();
            var emps = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
            return run.Lines.Where(l => !l.IsExcluded).OrderBy(l => l.EmployeeName).Select(l => ToSlip(run, l, emps.GetValueOrDefault(l.EmployeeId))).ToList();
        }

        public async Task<HrPayslipDto?> PayslipAsync(int runId, int employeeId, CancellationToken ct = default)
        {
            var run = await Runs().AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
            var line = run?.Lines.FirstOrDefault(l => l.EmployeeId == employeeId);
            if (run is null || line is null) return null;
            var emp = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
            return ToSlip(run, line, emp);
        }

        public string PayslipHtml(HrPayslipDto s)
        {
            var inv = CultureInfo.InvariantCulture;
            string H(string? v) => WebUtility.HtmlEncode(v ?? "");
            string Money(decimal v) => v.ToString("N2", inv);
            var monthName = new DateTime(s.Year, s.Month, 1).ToString("MMMM yyyy", inv);
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html><head><meta charset=\"utf-8\"><title>Payslip ").Append(H(s.EmployeeName)).Append(" ").Append(monthName).Append("</title>");
            sb.Append("<style>body{font-family:Arial,Helvetica,sans-serif;color:#111;margin:32px;max-width:720px}h1{font-size:20px;margin:0 0 4px}h2{font-size:14px;margin:18px 0 6px;text-transform:uppercase;color:#555}table{width:100%;border-collapse:collapse}td,th{padding:6px 8px;border-bottom:1px solid #ddd;text-align:left;font-size:13px}td.n,th.n{text-align:right}tr.total td{font-weight:bold;border-top:2px solid #333}.meta{color:#555;font-size:13px}.badge{display:inline-block;padding:2px 8px;border-radius:10px;background:#eee;font-size:12px}.net{font-size:18px;font-weight:bold;margin-top:16px}@media print{body{margin:0}button{display:none}}</style></head><body>");
            sb.Append("<h1>Payslip &mdash; ").Append(monthName).Append("</h1>");
            sb.Append("<div class=\"meta\">").Append(H(s.EmployeeName));
            if (!string.IsNullOrEmpty(s.Position)) sb.Append(" &middot; ").Append(H(s.Position));
            if (!string.IsNullOrEmpty(s.Department)) sb.Append(" &middot; ").Append(H(s.Department));
            sb.Append(" &middot; Run #").Append(s.RunId).Append(" <span class=\"badge\">").Append(H(s.RunStatus)).Append("</span></div>");
            if (s.IsExcluded) sb.Append("<p><b>Excluded from this run.</b></p>");

            sb.Append("<h2>Earnings</h2><table>");
            if (s.MonthlySalary is > 0)
                sb.Append("<tr><td>Monthly salary</td><td class=\"n\">").Append(Money(s.BasePay)).Append("</td></tr>");
            else
                sb.Append("<tr><td>Base pay (").Append(s.HoursWorked.ToString("0.##", inv)).Append(" h &times; ").Append(Money(s.HourlyRate ?? 0)).Append("/h)</td><td class=\"n\">").Append(Money(s.BasePay)).Append("</td></tr>");
            if (s.OvertimePay != 0)
                sb.Append("<tr><td>Overtime (").Append(s.OvertimeHours.ToString("0.##", inv)).Append(" h &times; 1.5)</td><td class=\"n\">").Append(Money(s.OvertimePay)).Append("</td></tr>");
            foreach (var a in s.Allowances)
                sb.Append("<tr><td>").Append(H(a.Name)).Append(a.Method == PayComponentType.MethodPercentOfGross ? $" ({a.Rate.ToString("0.##", inv)}%)" : "").Append("</td><td class=\"n\">").Append(Money(a.Amount)).Append("</td></tr>");
            sb.Append("<tr class=\"total\"><td>Gross</td><td class=\"n\">").Append(Money(s.Gross)).Append("</td></tr></table>");

            sb.Append("<h2>Deductions</h2><table>");
            if (s.Deductions.Count == 0) sb.Append("<tr><td colspan=\"2\">None</td></tr>");
            foreach (var d in s.Deductions)
                sb.Append("<tr><td>").Append(H(d.Name)).Append(d.Method == PayComponentType.MethodPercentOfGross ? $" ({d.Rate.ToString("0.##", inv)}%)" : "").Append(d.IsStatutory ? " <span class=\"badge\">statutory</span>" : "").Append("</td><td class=\"n\">").Append(Money(d.Amount)).Append("</td></tr>");
            sb.Append("<tr class=\"total\"><td>Total deductions</td><td class=\"n\">").Append(Money(s.TotalDeductions)).Append("</td></tr></table>");

            sb.Append("<div class=\"net\">Net pay: ").Append(Money(s.Net)).Append("</div>");
            if (!string.IsNullOrEmpty(s.Notes)) sb.Append("<p class=\"meta\">").Append(H(s.Notes)).Append("</p>");
            sb.Append("<p><button onclick=\"window.print()\">Print</button></p></body></html>");
            return sb.ToString();
        }

        public const string CsvHeader = "employee,position,basePay,hoursWorked,overtimeHours,overtimePay,allowances,deductions,gross,net,excluded";

        public async Task<string?> ExportCsvAsync(int runId, CancellationToken ct = default)
        {
            var run = await Runs().AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct);
            if (run is null) return null;
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine(CsvHeader);
            foreach (var l in run.Lines.OrderBy(l => l.EmployeeName).ThenBy(l => l.EmployeeId))
            {
                sb.Append(Csv(l.EmployeeName)).Append(',').Append(Csv(l.Position ?? "")).Append(',')
                  .Append(l.BasePay.ToString("0.00", inv)).Append(',')
                  .Append(l.HoursWorked.ToString("0.00", inv)).Append(',')
                  .Append(l.OvertimeHours.ToString("0.00", inv)).Append(',')
                  .Append(l.OvertimePay.ToString("0.00", inv)).Append(',')
                  .Append(l.Allowances.ToString("0.00", inv)).Append(',')
                  .Append(l.Deductions.ToString("0.00", inv)).Append(',')
                  .Append(l.Gross.ToString("0.00", inv)).Append(',')
                  .Append(l.Net.ToString("0.00", inv)).Append(',')
                  .Append(l.IsExcluded ? "yes" : "no").AppendLine();
            }
            return sb.ToString();
        }

        private static string Csv(string s)
            => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
