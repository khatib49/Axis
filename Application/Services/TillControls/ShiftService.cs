using System.Text.Json;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.TillControls
{
    public class ShiftService : IShiftService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly IShiftReportService _reports;
        private readonly ILogger<ShiftService> _log;

        public ShiftService(ApplicationDbContext db, ICurrentUserAccessor user, IShiftReportService reports, ILogger<ShiftService> log)
        {
            _db = db;
            _user = user;
            _reports = reports;
            _log = log;
        }

        public async Task<BaseResponse<CashierShiftDto>> OpenAsync(OpenShiftDto dto, CancellationToken ct = default)
        {
            if (_user.UserId is null) return Fail<CashierShiftDto>("Sign in to open a shift.");
            var register = (dto.RegisterName ?? string.Empty).Trim();
            if (register.Length == 0) return Fail<CashierShiftDto>("Register name is required.");
            if (dto.OpeningFloat < 0) return Fail<CashierShiftDto>("Opening float cannot be negative.");

            var userId = _user.UserId.Value;
            var existing = await _db.Set<CashierShift>().AsNoTracking()
                .FirstOrDefaultAsync(s => s.UserId == userId && s.RegisterName == register && s.Status == ShiftStatuses.Open, ct);
            if (existing is not null)
                return Fail<CashierShiftDto>($"You already have an open shift on '{register}' (opened {existing.OpenedOn:u}). Close it first.");

            var e = new CashierShift
            {
                RegisterName = register,
                UserId = userId,
                UserName = _user.UserName,
                OpenedOn = DateTime.UtcNow,
                OpeningFloat = dto.OpeningFloat,
                Status = ShiftStatuses.Open,
                Notes = dto.Notes,
                BranchId = _user.BranchId,
            };
            _db.Set<CashierShift>().Add(e);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Shift {Id} opened by {User} on {Register} with float {Float}", e.Id, e.UserName, register, dto.OpeningFloat);
            return Ok(ToDto(e), "Shift opened.");
        }

        public async Task<CashierShiftDto?> GetCurrentAsync(string? registerName = null, CancellationToken ct = default)
        {
            var e = await FindCurrentAsync(registerName, track: false, ct);
            return e is null ? null : ToDto(e);
        }

        public Task<BaseResponse<CashMovementDto>> PayInAsync(CashMovementCreateDto dto, int? shiftId = null, CancellationToken ct = default)
            => AddMovementAsync(CashMovementTypes.PayIn, dto, shiftId, ct);

        public Task<BaseResponse<CashMovementDto>> PayOutAsync(CashMovementCreateDto dto, int? shiftId = null, CancellationToken ct = default)
            => AddMovementAsync(CashMovementTypes.PayOut, dto, shiftId, ct);

        private async Task<BaseResponse<CashMovementDto>> AddMovementAsync(string type, CashMovementCreateDto dto, int? shiftId, CancellationToken ct)
        {
            if (dto.Amount <= 0) return Fail<CashMovementDto>("Amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(dto.Reason)) return Fail<CashMovementDto>("Reason is required.");
            var shift = await ResolveOpenShiftAsync(shiftId, ct);
            if (shift is null) return Fail<CashMovementDto>("No open shift. Open a shift first.");

            var m = new CashMovement
            {
                ShiftId = shift.Id,
                Type = type,
                Amount = dto.Amount,
                Reason = dto.Reason.Trim(),
                CreatedOn = DateTime.UtcNow,
                CreatedBy = _user.UserName,
            };
            _db.Set<CashMovement>().Add(m);
            await _db.SaveChangesAsync(ct);
            return Ok(ToDto(m), $"{type} recorded.");
        }

        public async Task<BaseResponse<ShiftReportDto>> XReportAsync(int? shiftId = null, CancellationToken ct = default)
        {
            var shift = await ResolveOpenShiftAsync(shiftId, ct);
            if (shift is null) return Fail<ShiftReportDto>("No open shift.");
            return Ok(await _reports.BuildAsync(shift, "X", ct), "OK");
        }

        public async Task<BaseResponse<ShiftReportDto>> CloseAsync(CloseShiftDto dto, int? shiftId = null, CancellationToken ct = default)
        {
            if (dto.CountedCash < 0) return Fail<ShiftReportDto>("Counted cash cannot be negative.");
            var shift = await ResolveOpenShiftAsync(shiftId, ct, track: true);
            if (shift is null) return Fail<ShiftReportDto>("No open shift to close.");

            shift.ClosedOn = DateTime.UtcNow;
            shift.ClosedBy = _user.UserName;
            shift.CountedCash = dto.CountedCash;
            if (!string.IsNullOrWhiteSpace(dto.Notes))
                shift.Notes = string.IsNullOrWhiteSpace(shift.Notes) ? dto.Notes : $"{shift.Notes}\n{dto.Notes}";
            shift.Status = ShiftStatuses.Closed;

            var report = await _reports.BuildAsync(shift, "Z", ct);
            shift.ExpectedCash = report.ExpectedCash;
            shift.Variance = report.Variance ?? (dto.CountedCash - report.ExpectedCash);
            shift.ReportJson = JsonSerializer.Serialize(report);
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("Shift {Id} closed by {User}: expected {Expected}, counted {Counted}, variance {Variance}",
                shift.Id, _user.UserName, shift.ExpectedCash, shift.CountedCash, shift.Variance);
            return Ok(report, "Shift closed.");
        }

        public async Task<PaginatedResponse<CashierShiftDto>> ListAsync(ShiftFilterDto filter, CancellationToken ct = default)
        {
            var q = _db.Set<CashierShift>().AsNoTracking().Include(s => s.Movements).AsQueryable();
            if (filter.UserId.HasValue) q = q.Where(s => s.UserId == filter.UserId.Value);
            if (!string.IsNullOrWhiteSpace(filter.RegisterName)) q = q.Where(s => s.RegisterName == filter.RegisterName.Trim());
            if (!string.IsNullOrWhiteSpace(filter.Status)) q = q.Where(s => s.Status == filter.Status.Trim());
            if (filter.From.HasValue) q = q.Where(s => s.OpenedOn >= filter.From.Value);
            if (filter.To.HasValue) q = q.Where(s => s.OpenedOn <= filter.To.Value);

            var page = Math.Max(1, filter.Page);
            var size = Math.Clamp(filter.PageSize, 1, 200);
            var total = await q.CountAsync(ct);
            var list = await q.OrderByDescending(s => s.OpenedOn).Skip((page - 1) * size).Take(size).ToListAsync(ct);
            return new PaginatedResponse<CashierShiftDto>(total, list.Select(ToDto).ToList(), page, size);
        }

        public async Task<CashierShiftDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<CashierShift>().AsNoTracking().Include(s => s.Movements).FirstOrDefaultAsync(s => s.Id == id, ct);
            return e is null ? null : ToDto(e);
        }

        public async Task<ShiftReportDto?> ZReportAsync(int shiftId, CancellationToken ct = default)
        {
            var e = await _db.Set<CashierShift>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == shiftId, ct);
            if (e is null) return null;
            if (e.Status != ShiftStatuses.Closed) return await _reports.BuildAsync(e, "X", ct);
            if (!string.IsNullOrWhiteSpace(e.ReportJson))
            {
                try
                {
                    var frozen = JsonSerializer.Deserialize<ShiftReportDto>(e.ReportJson);
                    if (frozen is not null) return frozen;
                }
                catch (JsonException ex)
                {
                    _log.LogWarning(ex, "Stored Z report for shift {Id} is unreadable; recomputing", shiftId);
                }
            }
            return await _reports.BuildAsync(e, "Z", ct);
        }

        // ── helpers ──────────────────────────────────────────────
        private async Task<CashierShift?> FindCurrentAsync(string? registerName, bool track, CancellationToken ct)
        {
            if (_user.UserId is null) return null;
            var userId = _user.UserId.Value;
            var q = track ? _db.Set<CashierShift>().AsQueryable() : _db.Set<CashierShift>().AsNoTracking();
            q = q.Include(s => s.Movements).Where(s => s.UserId == userId && s.Status == ShiftStatuses.Open);
            if (!string.IsNullOrWhiteSpace(registerName)) q = q.Where(s => s.RegisterName == registerName.Trim());
            return await q.OrderByDescending(s => s.OpenedOn).FirstOrDefaultAsync(ct);
        }

        private async Task<CashierShift?> ResolveOpenShiftAsync(int? shiftId, CancellationToken ct, bool track = false)
        {
            if (shiftId is null) return await FindCurrentAsync(null, track, ct);
            var q = track ? _db.Set<CashierShift>().AsQueryable() : _db.Set<CashierShift>().AsNoTracking();
            var s = await q.Include(x => x.Movements).FirstOrDefaultAsync(x => x.Id == shiftId.Value, ct);
            if (s is null || s.Status != ShiftStatuses.Open) return null;
            // A cashier may only act on their own shift; admins may act on any.
            if (s.UserId != _user.UserId && !_user.IsInRole("admin")) return null;
            return s;
        }

        internal static CashierShiftDto ToDto(CashierShift s) => new(
            s.Id, s.RegisterName, s.UserId, s.UserName, s.OpenedOn, s.ClosedOn, s.OpeningFloat,
            s.ExpectedCash, s.CountedCash, s.Variance, s.Status, s.Notes, s.ClosedBy, s.BranchId,
            (s.Movements ?? new List<CashMovement>()).OrderBy(m => m.CreatedOn).Select(ToDto).ToList());

        internal static CashMovementDto ToDto(CashMovement m)
            => new(m.Id, m.ShiftId, m.Type, m.Amount, m.Reason, m.CreatedOn, m.CreatedBy);

        private static BaseResponse<T> Fail<T>(string msg) => new(false, msg, msg);
        private static BaseResponse<T> Ok<T>(T data, string msg) => new(true, null, msg, data);
    }
}
