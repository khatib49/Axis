using System.Globalization;
using System.Text;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Hr
{
    public class HrAttendanceService : IHrAttendanceService
    {
        /// <summary>A clock-in links to a scheduled shift when it starts within this window.</summary>
        public static readonly TimeSpan ShiftLinkWindow = TimeSpan.FromHours(2);

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<HrAttendanceService> _log;
        private readonly Func<DateTime> _now;

        public HrAttendanceService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<HrAttendanceService> log)
            : this(db, user, log, () => DateTime.UtcNow) { }

        /// <summary>Test seam: fixed clock.</summary>
        internal HrAttendanceService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<HrAttendanceService> log, Func<DateTime> now)
        {
            _db = db; _user = user; _log = log; _now = now;
        }

        private static HrAttendanceDto ToDto(AttendanceRecord r, string name) => new(
            r.Id, r.EmployeeId, name, r.ClockIn, r.ClockOut, r.Source, r.ScheduledShiftId, r.DeviceName, r.Notes, r.WorkedMinutes, r.BranchId);

        private static DateTime Utc(DateTime d) => DateTime.SpecifyKind(d, DateTimeKind.Utc);
        private static DateTime DayStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        // ── Till clock ────────────────────────────────────────────────────
        public async Task<HrClockResultDto> ClockAsync(HrClockRequestDto dto, CancellationToken ct = default)
        {
            if (!HrPinHasher.IsValidPin(dto.Pin))
                return new HrClockResultDto(false, "Enter a 4-6 digit PIN", null, null, null, null, null);

            var candidates = await _db.Set<Employee>().AsNoTracking()
                .Where(e => e.IsActive && e.PinHash != null).ToListAsync(ct);
            var emp = candidates.FirstOrDefault(e => HrPinHasher.Verify(dto.Pin, e.PinHash, e.PinSalt));
            if (emp is null)
            {
                _log.LogWarning("HR clock: unknown PIN from device {Device}", dto.DeviceName);
                return new HrClockResultDto(false, "PIN not recognised", null, null, null, null, null);
            }

            var now = _now();
            var open = await _db.Set<AttendanceRecord>()
                .Where(r => r.EmployeeId == emp.Id && r.ClockOut == null)
                .OrderByDescending(r => r.ClockIn).FirstOrDefaultAsync(ct);

            if (open != null)
            {
                open.ClockOut = now;
                open.WorkedMinutes = HrCalculations.WorkedMinutes(open.ClockIn, now);
                await _db.SaveChangesAsync(ct);
                return new HrClockResultDto(true, $"Goodbye {emp.FullName}", emp.Id, emp.FullName, "ClockOut", now, open.WorkedMinutes);
            }

            var rec = new AttendanceRecord
            {
                EmployeeId = emp.Id, ClockIn = now, Source = AttendanceRecord.SourcePin, DeviceName = dto.DeviceName,
                BranchId = _user.BranchId, ScheduledShiftId = await NearestShiftIdAsync(emp.Id, now, ct),
            };
            _db.Add(rec);
            await _db.SaveChangesAsync(ct);
            return new HrClockResultDto(true, $"Welcome {emp.FullName}", emp.Id, emp.FullName, "ClockIn", now, null);
        }

        private async Task<int?> NearestShiftIdAsync(int employeeId, DateTime at, CancellationToken ct)
        {
            var today = DateOnly.FromDateTime(at);
            var yesterday = today.AddDays(-1);
            var shifts = await _db.Set<ScheduledShift>().AsNoTracking()
                .Where(s => s.EmployeeId == employeeId && (s.Date == today || s.Date == yesterday) && s.Status != ScheduledShift.StatusCancelled)
                .ToListAsync(ct);
            return shifts
                .Select(s => (s.Id, Diff: (s.StartOn - at).Duration()))
                .Where(x => x.Diff <= ShiftLinkWindow)
                .OrderBy(x => x.Diff)
                .Select(x => (int?)x.Id).FirstOrDefault();
        }

        // ── Admin maintenance ─────────────────────────────────────────────
        public async Task<BaseResponse<HrAttendanceDto>> AddManualAsync(HrAttendanceManualDto dto, CancellationToken ct = default)
        {
            var emp = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
            if (emp is null) return new BaseResponse<HrAttendanceDto>(false, "validation", "Employee not found");
            var cin = Utc(dto.ClockIn);
            DateTime? cout = dto.ClockOut.HasValue ? Utc(dto.ClockOut.Value) : null;
            if (cout.HasValue && cout <= cin) return new BaseResponse<HrAttendanceDto>(false, "validation", "Clock out must be after clock in");
            var rec = new AttendanceRecord
            {
                EmployeeId = emp.Id, ClockIn = cin, ClockOut = cout, Source = AttendanceRecord.SourceManual, Notes = dto.Notes,
                ScheduledShiftId = dto.ScheduledShiftId ?? await NearestShiftIdAsync(emp.Id, cin, ct),
                WorkedMinutes = cout.HasValue ? HrCalculations.WorkedMinutes(cin, cout.Value) : null,
                BranchId = _user.BranchId, DeviceName = _user.UserName,
            };
            _db.Add(rec);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<HrAttendanceDto>(true, null, "Added", ToDto(rec, emp.FullName));
        }

        public async Task<BaseResponse<HrAttendanceDto>> UpdateAsync(int id, HrAttendanceUpdateDto dto, CancellationToken ct = default)
        {
            var rec = await _db.Set<AttendanceRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rec is null) return new BaseResponse<HrAttendanceDto>(false, "not_found", "Record not found");
            if (dto.ClockIn.HasValue) rec.ClockIn = Utc(dto.ClockIn.Value);
            if (dto.ClockOut.HasValue) rec.ClockOut = Utc(dto.ClockOut.Value);
            if (dto.ScheduledShiftId.HasValue) rec.ScheduledShiftId = dto.ScheduledShiftId;
            if (dto.Notes != null) rec.Notes = dto.Notes;
            if (rec.ClockOut.HasValue && rec.ClockOut <= rec.ClockIn) return new BaseResponse<HrAttendanceDto>(false, "validation", "Clock out must be after clock in");
            rec.WorkedMinutes = rec.ClockOut.HasValue ? HrCalculations.WorkedMinutes(rec.ClockIn, rec.ClockOut.Value) : null;
            rec.Source = AttendanceRecord.SourceAdmin;
            await _db.SaveChangesAsync(ct);
            var name = await _db.Set<Employee>().AsNoTracking().Where(e => e.Id == rec.EmployeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? "";
            return new BaseResponse<HrAttendanceDto>(true, null, "Updated", ToDto(rec, name));
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var rec = await _db.Set<AttendanceRecord>().FirstOrDefaultAsync(r => r.Id == id, ct);
            if (rec is null) return false;
            _db.Remove(rec);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // ── Queries ───────────────────────────────────────────────────────
        private IQueryable<AttendanceRecord> Range(DateOnly from, DateOnly to, int? employeeId)
        {
            var start = DayStart(from);
            var end = DayStart(to.AddDays(1));
            var q = _db.Set<AttendanceRecord>().AsNoTracking().Where(r => r.ClockIn >= start && r.ClockIn < end);
            if (employeeId.HasValue) q = q.Where(r => r.EmployeeId == employeeId);
            return q;
        }

        public async Task<List<HrAttendanceDto>> ListAsync(DateOnly from, DateOnly to, int? employeeId, CancellationToken ct = default)
        {
            var rows = await Range(from, to, employeeId).OrderByDescending(r => r.ClockIn).ToListAsync(ct);
            var names = await NamesAsync(rows.Select(r => r.EmployeeId), ct);
            return rows.Select(r => ToDto(r, names.GetValueOrDefault(r.EmployeeId, ""))).ToList();
        }

        private async Task<Dictionary<int, string>> NamesAsync(IEnumerable<int> ids, CancellationToken ct)
        {
            var set = ids.Distinct().ToList();
            return await _db.Set<Employee>().AsNoTracking().Where(e => set.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        }

        public async Task<List<HrDailySummaryRowDto>> DailySummaryAsync(DateOnly date, CancellationToken ct = default)
        {
            var employees = await _db.Set<Employee>().AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct);
            var records = await Range(date, date, null).ToListAsync(ct);
            var shifts = await _db.Set<ScheduledShift>().AsNoTracking()
                .Where(s => s.Date == date && s.Status != ScheduledShift.StatusCancelled).ToListAsync(ct);

            var rows = new List<HrDailySummaryRowDto>();
            foreach (var e in employees)
            {
                var recs = records.Where(r => r.EmployeeId == e.Id).OrderBy(r => r.ClockIn).ToList();
                var shift = shifts.Where(s => s.EmployeeId == e.Id).OrderBy(s => s.StartOn).FirstOrDefault();
                if (recs.Count == 0 && shift is null) continue; // nothing to say about this person today
                var first = recs.FirstOrDefault()?.ClockIn;
                var last = recs.All(r => r.ClockOut.HasValue) ? recs.LastOrDefault()?.ClockOut : null;
                var worked = recs.Sum(r => r.WorkedMinutes ?? 0);
                var late = (shift != null && first.HasValue) ? HrCalculations.LateMinutes(shift.StartOn, first.Value) : 0;
                var status = recs.Count == 0 ? "Absent" : recs.Any(r => r.ClockOut == null) ? "Open" : "Present";
                rows.Add(new HrDailySummaryRowDto(e.Id, e.FullName, date, shift?.StartOn, shift?.EndOn, first, last, worked, late, status));
            }
            return rows;
        }

        public async Task<HrTimesheetDto?> TimesheetAsync(int employeeId, int year, int month, CancellationToken ct = default)
        {
            var emp = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
            if (emp is null) return null;
            var from = new DateOnly(year, month, 1);
            var to = from.AddMonths(1).AddDays(-1);
            var recs = await Range(from, to, employeeId).OrderBy(r => r.ClockIn).ToListAsync(ct);
            var rate = emp.HourlyRate ?? 0m;
            var rows = recs.Select(r =>
            {
                var mins = r.WorkedMinutes ?? 0;
                return new HrTimesheetRowDto(DateOnly.FromDateTime(r.ClockIn), r.ClockIn, r.ClockOut, mins, Math.Round(rate * mins / 60m, 2));
            }).ToList();
            var totalMin = rows.Sum(r => r.Minutes);
            return new HrTimesheetDto(emp.Id, emp.FullName, year, month, emp.HourlyRate, totalMin, Math.Round(rate * totalMin / 60m, 2), rows);
        }

        public async Task<string> ExportCsvAsync(DateOnly from, DateOnly to, int? employeeId, CancellationToken ct = default)
        {
            var recs = await Range(from, to, employeeId).OrderBy(r => r.EmployeeId).ThenBy(r => r.ClockIn).ToListAsync(ct);
            var ids = recs.Select(r => r.EmployeeId).Distinct().ToList();
            var emps = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, ct);
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.AppendLine("employee,date,in,out,minutes,hourlyRate,amount");
            foreach (var r in recs)
            {
                emps.TryGetValue(r.EmployeeId, out var e);
                var rate = e?.HourlyRate ?? 0m;
                var mins = r.WorkedMinutes ?? 0;
                sb.Append(Csv(e?.FullName ?? r.EmployeeId.ToString(inv))).Append(',')
                  .Append(DateOnly.FromDateTime(r.ClockIn).ToString("yyyy-MM-dd", inv)).Append(',')
                  .Append(r.ClockIn.ToString("HH:mm", inv)).Append(',')
                  .Append(r.ClockOut?.ToString("HH:mm", inv) ?? "").Append(',')
                  .Append(mins.ToString(inv)).Append(',')
                  .Append(rate.ToString("0.00", inv)).Append(',')
                  .Append(Math.Round(rate * mins / 60m, 2).ToString("0.00", inv)).AppendLine();
            }
            return sb.ToString();
        }

        private static string Csv(string s)
            => s.Contains(',') || s.Contains('"') || s.Contains('\n') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
