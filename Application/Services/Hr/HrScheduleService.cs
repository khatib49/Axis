using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Hr
{
    public class HrScheduleService : IHrScheduleService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<HrScheduleService> _log;

        public HrScheduleService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<HrScheduleService> log)
        {
            _db = db; _user = user; _log = log;
        }

        private static readonly string[] Statuses = { ScheduledShift.StatusPlanned, ScheduledShift.StatusConfirmed, ScheduledShift.StatusCancelled };

        private static HrShiftTemplateDto ToDto(ShiftTemplate t) => new(t.Id, t.Name, t.StartTime, t.EndTime, t.Color);
        private static HrShiftDto ToDto(ScheduledShift s, string employeeName) => new(
            s.Id, s.EmployeeId, employeeName, s.Date, s.StartOn, s.EndOn, s.TemplateId, s.Role, s.Notes, s.Status, s.CreatedBy);

        /// <summary>Monday of the week containing <paramref name="d"/>.</summary>
        public static DateOnly WeekStartOf(DateOnly d)
        {
            var diff = ((int)d.DayOfWeek + 6) % 7; // Monday = 0
            return d.AddDays(-diff);
        }

        /// <summary>Applies a template's times to a calendar day (UTC); an end &lt;= start rolls to the next day.</summary>
        public static (DateTime StartOn, DateTime EndOn) ApplyTemplate(DateOnly date, TimeOnly start, TimeOnly end)
        {
            var s = DateTime.SpecifyKind(date.ToDateTime(start), DateTimeKind.Utc);
            var e = DateTime.SpecifyKind(date.ToDateTime(end), DateTimeKind.Utc);
            if (e <= s) e = e.AddDays(1);
            return (s, e);
        }

        // ── Templates ─────────────────────────────────────────────────────
        public async Task<List<HrShiftTemplateDto>> ListTemplatesAsync(CancellationToken ct = default)
            => (await _db.Set<ShiftTemplate>().AsNoTracking().OrderBy(t => t.StartTime).ToListAsync(ct)).Select(ToDto).ToList();

        public async Task<HrShiftTemplateDto> CreateTemplateAsync(HrShiftTemplateUpsertDto dto, CancellationToken ct = default)
        {
            var t = new ShiftTemplate { Name = dto.Name.Trim(), StartTime = dto.StartTime, EndTime = dto.EndTime, Color = dto.Color };
            _db.Add(t);
            await _db.SaveChangesAsync(ct);
            return ToDto(t);
        }

        public async Task<bool> UpdateTemplateAsync(int id, HrShiftTemplateUpsertDto dto, CancellationToken ct = default)
        {
            var t = await _db.Set<ShiftTemplate>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return false;
            t.Name = dto.Name.Trim(); t.StartTime = dto.StartTime; t.EndTime = dto.EndTime; t.Color = dto.Color;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteTemplateAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<ShiftTemplate>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return false;
            _db.Remove(t);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // ── Week view ─────────────────────────────────────────────────────
        public async Task<HrWeekViewDto> GetWeekAsync(DateOnly? weekStart, CancellationToken ct = default)
        {
            var start = WeekStartOf(weekStart ?? DateOnly.FromDateTime(DateTime.UtcNow));
            var employees = await _db.Set<Employee>().AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct);
            return await BuildWeekAsync(start, employees, ct);
        }

        public async Task<HrWeekViewDto?> GetMyWeekAsync(DateOnly? weekStart, CancellationToken ct = default)
        {
            var uid = _user.UserId;
            if (uid is null) return null;
            var me = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.UserId == uid, ct);
            if (me is null) return null;
            var start = WeekStartOf(weekStart ?? DateOnly.FromDateTime(DateTime.UtcNow));
            return await BuildWeekAsync(start, new List<Employee> { me }, ct);
        }

        private async Task<HrWeekViewDto> BuildWeekAsync(DateOnly start, List<Employee> employees, CancellationToken ct)
        {
            var end = start.AddDays(6);
            var ids = employees.Select(e => e.Id).ToList();
            var shifts = await _db.Set<ScheduledShift>().AsNoTracking()
                .Where(s => s.Date >= start && s.Date <= end && ids.Contains(s.EmployeeId))
                .OrderBy(s => s.StartOn).ToListAsync(ct);
            var days = Enumerable.Range(0, 7).Select(i => start.AddDays(i)).ToList();
            var rows = employees.Select(e => new HrWeekRowDto(
                e.Id, e.FullName, e.Position,
                shifts.Where(s => s.EmployeeId == e.Id).Select(s => ToDto(s, e.FullName)).ToList())).ToList();
            return new HrWeekViewDto(start, days, rows);
        }

        // ── Shifts ────────────────────────────────────────────────────────
        public async Task<BaseResponse<HrShiftDto>> CreateShiftAsync(HrShiftCreateDto dto, CancellationToken ct = default)
        {
            var emp = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId, ct);
            if (emp is null) return new BaseResponse<HrShiftDto>(false, "validation", "Employee not found");

            DateTime startOn, endOn;
            if (dto.StartOn.HasValue && dto.EndOn.HasValue)
            {
                startOn = DateTime.SpecifyKind(dto.StartOn.Value, DateTimeKind.Utc);
                endOn = DateTime.SpecifyKind(dto.EndOn.Value, DateTimeKind.Utc);
            }
            else if (dto.TemplateId.HasValue)
            {
                var t = await _db.Set<ShiftTemplate>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.TemplateId, ct);
                if (t is null) return new BaseResponse<HrShiftDto>(false, "validation", "Template not found");
                (startOn, endOn) = ApplyTemplate(dto.Date, t.StartTime, t.EndTime);
            }
            else return new BaseResponse<HrShiftDto>(false, "validation", "Give StartOn/EndOn or a TemplateId");
            if (endOn <= startOn) return new BaseResponse<HrShiftDto>(false, "validation", "End must be after start");

            var s = new ScheduledShift
            {
                EmployeeId = dto.EmployeeId, Date = dto.Date, StartOn = startOn, EndOn = endOn, TemplateId = dto.TemplateId,
                Role = dto.Role ?? emp.Position, Notes = dto.Notes, Status = ScheduledShift.StatusPlanned,
                CreatedBy = _user.UserName, BranchId = _user.BranchId,
            };
            _db.Add(s);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<HrShiftDto>(true, null, "Created", ToDto(s, emp.FullName));
        }

        public async Task<BaseResponse<HrShiftDto>> UpdateShiftAsync(int id, HrShiftUpdateDto dto, CancellationToken ct = default)
        {
            var s = await _db.Set<ScheduledShift>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return new BaseResponse<HrShiftDto>(false, "not_found", "Shift not found");
            if (dto.EmployeeId.HasValue)
            {
                if (!await _db.Set<Employee>().AnyAsync(e => e.Id == dto.EmployeeId, ct))
                    return new BaseResponse<HrShiftDto>(false, "validation", "Employee not found");
                s.EmployeeId = dto.EmployeeId.Value;
            }
            if (dto.Date.HasValue) s.Date = dto.Date.Value;
            if (dto.TemplateId.HasValue)
            {
                var t = await _db.Set<ShiftTemplate>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.TemplateId, ct);
                if (t is null) return new BaseResponse<HrShiftDto>(false, "validation", "Template not found");
                s.TemplateId = t.Id;
                if (!dto.StartOn.HasValue && !dto.EndOn.HasValue) (s.StartOn, s.EndOn) = ApplyTemplate(s.Date, t.StartTime, t.EndTime);
            }
            if (dto.StartOn.HasValue) s.StartOn = DateTime.SpecifyKind(dto.StartOn.Value, DateTimeKind.Utc);
            if (dto.EndOn.HasValue) s.EndOn = DateTime.SpecifyKind(dto.EndOn.Value, DateTimeKind.Utc);
            if (s.EndOn <= s.StartOn) return new BaseResponse<HrShiftDto>(false, "validation", "End must be after start");
            if (dto.Role != null) s.Role = dto.Role;
            if (dto.Notes != null) s.Notes = dto.Notes;
            if (dto.Status != null)
            {
                if (!Statuses.Contains(dto.Status)) return new BaseResponse<HrShiftDto>(false, "validation", "Status must be Planned, Confirmed or Cancelled");
                s.Status = dto.Status;
            }
            await _db.SaveChangesAsync(ct);
            var name = await _db.Set<Employee>().AsNoTracking().Where(e => e.Id == s.EmployeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? "";
            return new BaseResponse<HrShiftDto>(true, null, "Updated", ToDto(s, name));
        }

        public async Task<bool> DeleteShiftAsync(int id, CancellationToken ct = default)
        {
            var s = await _db.Set<ScheduledShift>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return false;
            _db.Remove(s);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>Copies every non-cancelled shift of the week before <paramref name="weekStart"/> onto that week, skipping employees already scheduled on the target day.</summary>
        public async Task<BaseResponse<int>> CopyPreviousWeekAsync(DateOnly weekStart, CancellationToken ct = default)
        {
            var target = WeekStartOf(weekStart);
            var srcStart = target.AddDays(-7);
            var srcEnd = target.AddDays(-1);
            var source = await _db.Set<ScheduledShift>().AsNoTracking()
                .Where(s => s.Date >= srcStart && s.Date <= srcEnd && s.Status != ScheduledShift.StatusCancelled).ToListAsync(ct);
            if (source.Count == 0) return new BaseResponse<int>(false, "empty", "The previous week has no shifts to copy", 0);

            var activeIds = await _db.Set<Employee>().AsNoTracking().Where(e => e.IsActive).Select(e => e.Id).ToListAsync(ct);
            var targetEnd = target.AddDays(6);
            var existing = await _db.Set<ScheduledShift>().AsNoTracking()
                .Where(s => s.Date >= target && s.Date <= targetEnd)
                .Select(s => new { s.EmployeeId, s.Date }).ToListAsync(ct);
            var taken = existing.Select(x => (x.EmployeeId, x.Date)).ToHashSet();

            var created = 0;
            foreach (var s in source)
            {
                if (!activeIds.Contains(s.EmployeeId)) continue;
                var date = s.Date.AddDays(7);
                if (taken.Contains((s.EmployeeId, date))) continue;
                _db.Add(new ScheduledShift
                {
                    EmployeeId = s.EmployeeId, Date = date, StartOn = s.StartOn.AddDays(7), EndOn = s.EndOn.AddDays(7),
                    TemplateId = s.TemplateId, Role = s.Role, Notes = s.Notes, Status = ScheduledShift.StatusPlanned,
                    CreatedBy = _user.UserName, BranchId = s.BranchId ?? _user.BranchId,
                });
                taken.Add((s.EmployeeId, date));
                created++;
            }
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("HR schedule: copied {Count} shifts into week of {Week}", created, target);
            return new BaseResponse<int>(true, null, $"Copied {created} shift(s)", created);
        }
    }
}
