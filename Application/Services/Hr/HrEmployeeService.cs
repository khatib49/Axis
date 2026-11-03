using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Hr
{
    public class HrEmployeeService : IHrEmployeeService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<HrEmployeeService> _log;

        public HrEmployeeService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<HrEmployeeService> log)
        {
            _db = db; _user = user; _log = log;
        }

        internal static HrEmployeeDto ToDto(Employee e) => new(
            e.Id, e.UserId, e.FullName, e.Phone, e.Email, e.Position, e.Department, e.HourlyRate, e.MonthlySalary,
            e.HiredOn, e.TerminatedOn, e.IsActive, e.Notes, e.BranchId, !string.IsNullOrEmpty(e.PinHash));

        public async Task<PaginatedResponse<HrEmployeeDto>> ListAsync(string? search, bool includeInactive, int page, int pageSize, CancellationToken ct = default)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 20;
            var q = _db.Set<Employee>().AsNoTracking().AsQueryable();
            if (!includeInactive) q = q.Where(e => e.IsActive);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                q = q.Where(e => e.FullName.ToLower().Contains(s) || (e.Phone != null && e.Phone.Contains(s)));
            }
            var total = await q.CountAsync(ct);
            var rows = await q.OrderBy(e => e.FullName).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return new PaginatedResponse<HrEmployeeDto>(total, rows.Select(ToDto).ToList(), page, pageSize);
        }

        public async Task<HrEmployeeDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return e is null ? null : ToDto(e);
        }

        public async Task<HrEmployeeDto?> GetMineAsync(CancellationToken ct = default)
        {
            var uid = _user.UserId;
            if (uid is null) return null;
            var e = await _db.Set<Employee>().AsNoTracking().FirstOrDefaultAsync(x => x.UserId == uid, ct);
            return e is null ? null : ToDto(e);
        }

        public async Task<BaseResponse<HrEmployeeDto>> CreateAsync(HrEmployeeCreateDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.FullName))
                return new BaseResponse<HrEmployeeDto>(false, "validation", "Full name is required");
            if (dto.Pin != null && !HrPinHasher.IsValidPin(dto.Pin))
                return new BaseResponse<HrEmployeeDto>(false, "validation", "PIN must be 4-6 digits");
            if (dto.UserId.HasValue && await _db.Set<Employee>().AnyAsync(x => x.UserId == dto.UserId, ct))
                return new BaseResponse<HrEmployeeDto>(false, "validation", "That user is already linked to another employee");

            var e = new Employee
            {
                FullName = dto.FullName.Trim(),
                Phone = dto.Phone?.Trim(),
                Email = dto.Email?.Trim(),
                Position = dto.Position?.Trim(),
                Department = dto.Department?.Trim(),
                HourlyRate = dto.HourlyRate,
                MonthlySalary = dto.MonthlySalary,
                HiredOn = dto.HiredOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
                Notes = dto.Notes,
                UserId = dto.UserId,
                BranchId = _user.BranchId,
                IsActive = true,
            };
            if (dto.Pin != null)
            {
                e.PinSalt = HrPinHasher.NewSalt();
                e.PinHash = HrPinHasher.Hash(dto.Pin, e.PinSalt);
            }
            _db.Add(e);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("HR employee {Id} created by {User}", e.Id, _user.UserName);
            return new BaseResponse<HrEmployeeDto>(true, null, "Created", ToDto(e));
        }

        public async Task<BaseResponse<HrEmployeeDto>> UpdateAsync(int id, HrEmployeeUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _db.Set<Employee>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return new BaseResponse<HrEmployeeDto>(false, "not_found", "Employee not found");
            if (dto.FullName != null)
            {
                if (string.IsNullOrWhiteSpace(dto.FullName)) return new BaseResponse<HrEmployeeDto>(false, "validation", "Full name is required");
                e.FullName = dto.FullName.Trim();
            }
            if (dto.Phone != null) e.Phone = dto.Phone.Trim();
            if (dto.Email != null) e.Email = dto.Email.Trim();
            if (dto.Position != null) e.Position = dto.Position.Trim();
            if (dto.Department != null) e.Department = dto.Department.Trim();
            if (dto.HourlyRate.HasValue) e.HourlyRate = dto.HourlyRate;
            if (dto.MonthlySalary.HasValue) e.MonthlySalary = dto.MonthlySalary;
            if (dto.HiredOn.HasValue) e.HiredOn = dto.HiredOn.Value;
            if (dto.TerminatedOn.HasValue) e.TerminatedOn = dto.TerminatedOn;
            if (dto.IsActive.HasValue) e.IsActive = dto.IsActive.Value;
            if (dto.Notes != null) e.Notes = dto.Notes;
            e.UpdatedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<HrEmployeeDto>(true, null, "Updated", ToDto(e));
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<Employee>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return false;
            // Soft delete: attendance / leave history must survive.
            e.IsActive = false;
            e.TerminatedOn ??= DateOnly.FromDateTime(DateTime.UtcNow);
            e.PinHash = null; e.PinSalt = null;
            e.UpdatedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<BaseResponse<bool>> SetPinAsync(int id, string pin, CancellationToken ct = default)
        {
            if (!HrPinHasher.IsValidPin(pin)) return new BaseResponse<bool>(false, "validation", "PIN must be 4-6 digits");
            var e = await _db.Set<Employee>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return new BaseResponse<bool>(false, "not_found", "Employee not found");

            // A PIN must identify one person at the till, so refuse one already in use.
            var others = await _db.Set<Employee>().AsNoTracking()
                .Where(x => x.Id != id && x.IsActive && x.PinHash != null).ToListAsync(ct);
            if (others.Any(o => HrPinHasher.Verify(pin, o.PinHash, o.PinSalt)))
                return new BaseResponse<bool>(false, "validation", "That PIN is already used by another employee");

            e.PinSalt = HrPinHasher.NewSalt();
            e.PinHash = HrPinHasher.Hash(pin, e.PinSalt);
            e.UpdatedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("HR PIN set for employee {Id} by {User}", id, _user.UserName);
            return new BaseResponse<bool>(true, null, "PIN updated", true);
        }

        public async Task<BaseResponse<HrEmployeeDto>> LinkUserAsync(int id, int? userId, CancellationToken ct = default)
        {
            var e = await _db.Set<Employee>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return new BaseResponse<HrEmployeeDto>(false, "not_found", "Employee not found");
            if (userId.HasValue)
            {
                if (!await _db.Users.AnyAsync(u => u.Id == userId.Value, ct))
                    return new BaseResponse<HrEmployeeDto>(false, "validation", "User not found");
                if (await _db.Set<Employee>().AnyAsync(x => x.Id != id && x.UserId == userId, ct))
                    return new BaseResponse<HrEmployeeDto>(false, "validation", "That user is already linked to another employee");
            }
            e.UserId = userId;
            e.UpdatedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<HrEmployeeDto>(true, null, "Linked", ToDto(e));
        }
    }
}
