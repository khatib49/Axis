using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Hr
{
    public class HrLeaveService : IHrLeaveService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<HrLeaveService> _log;

        public HrLeaveService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<HrLeaveService> log)
        {
            _db = db; _user = user; _log = log;
        }

        private static HrLeaveTypeDto ToDto(LeaveType t) => new(t.Id, t.Name, t.IsPaid, t.DefaultDaysPerYear);
        private static HrLeaveRequestDto ToDto(LeaveRequest r, string emp, string type) => new(
            r.Id, r.EmployeeId, emp, r.LeaveTypeId, type, r.FromDate, r.ToDate, r.Days, r.Reason, r.Status,
            r.RequestedOn, r.DecidedBy, r.DecidedOn, r.DecisionNote);

        // ── Types ─────────────────────────────────────────────────────────
        public async Task<List<HrLeaveTypeDto>> ListTypesAsync(CancellationToken ct = default)
            => (await _db.Set<LeaveType>().AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct)).Select(ToDto).ToList();

        public async Task<HrLeaveTypeDto> CreateTypeAsync(HrLeaveTypeUpsertDto dto, CancellationToken ct = default)
        {
            var t = new LeaveType { Name = dto.Name.Trim(), IsPaid = dto.IsPaid, DefaultDaysPerYear = Math.Max(0, dto.DefaultDaysPerYear) };
            _db.Add(t);
            await _db.SaveChangesAsync(ct);
            return ToDto(t);
        }

        public async Task<bool> UpdateTypeAsync(int id, HrLeaveTypeUpsertDto dto, CancellationToken ct = default)
        {
            var t = await _db.Set<LeaveType>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return false;
            t.Name = dto.Name.Trim(); t.IsPaid = dto.IsPaid; t.DefaultDaysPerYear = Math.Max(0, dto.DefaultDaysPerYear);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteTypeAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<LeaveType>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return false;
            if (await _db.Set<LeaveRequest>().AnyAsync(r => r.LeaveTypeId == id, ct)) return false; // in use
            _db.Remove(t);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // ── Requests ──────────────────────────────────────────────────────
        private async Task<List<HrLeaveRequestDto>> ProjectAsync(IQueryable<LeaveRequest> q, CancellationToken ct)
        {
            var rows = await q.OrderByDescending(r => r.RequestedOn).ToListAsync(ct);
            var empIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
            var emps = await _db.Set<Employee>().AsNoTracking().Where(e => empIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
            var types = await _db.Set<LeaveType>().AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            return rows.Select(r => ToDto(r, emps.GetValueOrDefault(r.EmployeeId, ""), types.GetValueOrDefault(r.LeaveTypeId, ""))).ToList();
        }

        public Task<List<HrLeaveRequestDto>> ListRequestsAsync(string? status, int? employeeId, int? year, CancellationToken ct = default)
        {
            var q = _db.Set<LeaveRequest>().AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status);
            if (employeeId.HasValue) q = q.Where(r => r.EmployeeId == employeeId);
            if (year.HasValue) q = q.Where(r => r.FromDate.Year == year.Value || r.ToDate.Year == year.Value);
            return ProjectAsync(q, ct);
        }

        public async Task<List<HrLeaveRequestDto>> ListMyRequestsAsync(CancellationToken ct = default)
        {
            var me = await MyEmployeeIdAsync(ct);
            if (me is null) return new List<HrLeaveRequestDto>();
            return await ProjectAsync(_db.Set<LeaveRequest>().AsNoTracking().Where(r => r.EmployeeId == me), ct);
        }

        private async Task<int?> MyEmployeeIdAsync(CancellationToken ct)
        {
            var uid = _user.UserId;
            if (uid is null) return null;
            var id = await _db.Set<Employee>().AsNoTracking().Where(e => e.UserId == uid).Select(e => (int?)e.Id).FirstOrDefaultAsync(ct);
            return id;
        }

        public async Task<BaseResponse<HrLeaveRequestDto>> CreateRequestAsync(HrLeaveRequestCreateDto dto, bool isAdmin, CancellationToken ct = default)
        {
            int? employeeId = dto.EmployeeId;
            if (employeeId is null || !isAdmin)
            {
                var me = await MyEmployeeIdAsync(ct);
                if (me is null) return new BaseResponse<HrLeaveRequestDto>(false, "validation", "Your login is not linked to an employee profile");
                if (employeeId is null) employeeId = me;
                else if (employeeId != me) return new BaseResponse<HrLeaveRequestDto>(false, "forbidden", "You can only request leave for yourself");
            }
            var emp = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
            if (emp is null) return new BaseResponse<HrLeaveRequestDto>(false, "validation", "Employee not found");
            var type = await _db.Set<LeaveType>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.LeaveTypeId, ct);
            if (type is null) return new BaseResponse<HrLeaveRequestDto>(false, "validation", "Leave type not found");
            var days = HrCalculations.LeaveDays(dto.FromDate, dto.ToDate);
            if (days <= 0) return new BaseResponse<HrLeaveRequestDto>(false, "validation", "To date must not be before from date");

            var overlap = await _db.Set<LeaveRequest>().AsNoTracking().AnyAsync(r =>
                r.EmployeeId == emp.Id && r.Status != LeaveRequest.StatusRejected && r.Status != LeaveRequest.StatusCancelled &&
                r.FromDate <= dto.ToDate && r.ToDate >= dto.FromDate, ct);
            if (overlap) return new BaseResponse<HrLeaveRequestDto>(false, "validation", "An existing request already covers part of these dates");

            var r = new LeaveRequest
            {
                EmployeeId = emp.Id, LeaveTypeId = type.Id, FromDate = dto.FromDate, ToDate = dto.ToDate, Days = days,
                Reason = dto.Reason, Status = LeaveRequest.StatusPending, RequestedOn = DateTime.UtcNow,
            };
            _db.Add(r);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("HR leave request {Id} created for employee {Emp} by {User}", r.Id, emp.Id, _user.UserName);
            return new BaseResponse<HrLeaveRequestDto>(true, null, "Requested", ToDto(r, emp.FullName, type.Name));
        }

        public async Task<BaseResponse<HrLeaveRequestDto>> DecideAsync(int id, bool approve, string? note, CancellationToken ct = default)
        {
            var r = await _db.Set<LeaveRequest>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return new BaseResponse<HrLeaveRequestDto>(false, "not_found", "Request not found");
            if (r.Status != LeaveRequest.StatusPending) return new BaseResponse<HrLeaveRequestDto>(false, "validation", $"Request is already {r.Status}");
            r.Status = approve ? LeaveRequest.StatusApproved : LeaveRequest.StatusRejected;
            r.DecidedBy = _user.UserName; r.DecidedOn = DateTime.UtcNow; r.DecisionNote = note;
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<HrLeaveRequestDto>(true, null, r.Status, await OneAsync(r, ct));
        }

        public async Task<BaseResponse<HrLeaveRequestDto>> CancelAsync(int id, bool isAdmin, CancellationToken ct = default)
        {
            var r = await _db.Set<LeaveRequest>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return new BaseResponse<HrLeaveRequestDto>(false, "not_found", "Request not found");
            if (!isAdmin)
            {
                var me = await MyEmployeeIdAsync(ct);
                if (me != r.EmployeeId) return new BaseResponse<HrLeaveRequestDto>(false, "forbidden", "Not your request");
                if (r.Status != LeaveRequest.StatusPending) return new BaseResponse<HrLeaveRequestDto>(false, "validation", "Only pending requests can be cancelled");
            }
            if (r.Status == LeaveRequest.StatusCancelled) return new BaseResponse<HrLeaveRequestDto>(false, "validation", "Already cancelled");
            r.Status = LeaveRequest.StatusCancelled; r.DecidedBy = _user.UserName; r.DecidedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<HrLeaveRequestDto>(true, null, "Cancelled", await OneAsync(r, ct));
        }

        private async Task<HrLeaveRequestDto> OneAsync(LeaveRequest r, CancellationToken ct)
        {
            var emp = await _db.Set<Employee>().AsNoTracking().Where(e => e.Id == r.EmployeeId).Select(e => e.FullName).FirstOrDefaultAsync(ct) ?? "";
            var type = await _db.Set<LeaveType>().AsNoTracking().Where(t => t.Id == r.LeaveTypeId).Select(t => t.Name).FirstOrDefaultAsync(ct) ?? "";
            return ToDto(r, emp, type);
        }

        // ── Balances ──────────────────────────────────────────────────────
        /// <summary>Balance = type default days − approved days in the year (counted by FromDate year). Pending days shown separately.</summary>
        public async Task<HrLeaveBalanceDto?> BalanceAsync(int employeeId, int year, CancellationToken ct = default)
        {
            var emp = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
            if (emp is null) return null;
            var types = await _db.Set<LeaveType>().AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
            var reqs = await _db.Set<LeaveRequest>().AsNoTracking()
                .Where(r => r.EmployeeId == employeeId && r.FromDate.Year == year).ToListAsync(ct);
            return Build(emp, year, types, reqs);
        }

        public async Task<List<HrLeaveBalanceDto>> AllBalancesAsync(int year, CancellationToken ct = default)
        {
            var emps = await _db.Set<Employee>().AsNoTracking().Where(e => e.IsActive).OrderBy(e => e.FullName).ToListAsync(ct);
            var types = await _db.Set<LeaveType>().AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
            var reqs = await _db.Set<LeaveRequest>().AsNoTracking().Where(r => r.FromDate.Year == year).ToListAsync(ct);
            return emps.Select(e => Build(e, year, types, reqs.Where(r => r.EmployeeId == e.Id).ToList())).ToList();
        }

        internal static HrLeaveBalanceDto Build(Employee emp, int year, List<LeaveType> types, List<LeaveRequest> reqs)
        {
            var items = types.Select(t =>
            {
                var used = reqs.Where(r => r.LeaveTypeId == t.Id && r.Status == LeaveRequest.StatusApproved).Sum(r => r.Days);
                var pending = reqs.Where(r => r.LeaveTypeId == t.Id && r.Status == LeaveRequest.StatusPending).Sum(r => r.Days);
                return new HrLeaveBalanceItemDto(t.Id, t.Name, t.IsPaid, t.DefaultDaysPerYear, used, pending, t.DefaultDaysPerYear - used);
            }).ToList();
            return new HrLeaveBalanceDto(emp.Id, emp.FullName, year, items);
        }
    }
}
