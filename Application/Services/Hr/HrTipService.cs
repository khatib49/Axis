using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Hr
{
    public class HrTipService : IHrTipService
    {
        private static readonly string[] Methods = { TipPool.MethodEqual, TipPool.MethodByHours, TipPool.MethodByPoints };

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<HrTipService> _log;

        public HrTipService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<HrTipService> log)
        {
            _db = db; _user = user; _log = log;
        }

        private async Task<HrTipPoolDto> ToDtoAsync(TipPool p, CancellationToken ct)
        {
            var ids = p.Allocations.Select(a => a.EmployeeId).Distinct().ToList();
            var names = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
            return ToDto(p, names);
        }

        private static HrTipPoolDto ToDto(TipPool p, Dictionary<int, string> names) => new(
            p.Id, p.Date, p.TotalAmount, p.Method, p.Status, p.Notes, p.CreatedBy, p.CreatedOn, p.DistributedOn, p.BranchId,
            p.Allocations.OrderBy(a => names.GetValueOrDefault(a.EmployeeId, "")).Select(a =>
                new HrTipAllocationDto(a.Id, a.EmployeeId, names.GetValueOrDefault(a.EmployeeId, ""), a.Points, a.Hours, a.Amount)).ToList());

        private IQueryable<TipPool> Pools() => _db.Set<TipPool>().Include(p => p.Allocations);

        public async Task<List<HrTipPoolDto>> ListAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var pools = await Pools().AsNoTracking().Where(p => p.Date >= from && p.Date <= to).OrderByDescending(p => p.Date).ThenByDescending(p => p.Id).ToListAsync(ct);
            var ids = pools.SelectMany(p => p.Allocations).Select(a => a.EmployeeId).Distinct().ToList();
            var names = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
            return pools.Select(p => ToDto(p, names)).ToList();
        }

        public async Task<HrTipPoolDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var p = await Pools().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return p is null ? null : await ToDtoAsync(p, ct);
        }

        public async Task<BaseResponse<HrTipPoolDto>> CreateAsync(HrTipPoolCreateDto dto, CancellationToken ct = default)
        {
            if (dto.TotalAmount < 0) return new BaseResponse<HrTipPoolDto>(false, "validation", "Total must not be negative");
            if (!Methods.Contains(dto.Method)) return new BaseResponse<HrTipPoolDto>(false, "validation", "Method must be Equal, ByHours or ByPoints");
            var p = new TipPool
            {
                Date = dto.Date, TotalAmount = dto.TotalAmount, Method = dto.Method, Notes = dto.Notes,
                Status = TipPool.StatusDraft, CreatedBy = _user.UserName, BranchId = _user.BranchId,
            };
            var err = await SetParticipantsAsync(p, dto.Participants, ct);
            if (err != null) return new BaseResponse<HrTipPoolDto>(false, "validation", err);
            _db.Add(p);
            await _db.SaveChangesAsync(ct);
            await ComputeInternalAsync(p, ct);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<HrTipPoolDto>(true, null, "Created", await ToDtoAsync(p, ct));
        }

        public async Task<BaseResponse<HrTipPoolDto>> UpdateAsync(int id, HrTipPoolUpdateDto dto, CancellationToken ct = default)
        {
            var p = await Pools().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return new BaseResponse<HrTipPoolDto>(false, "not_found", "Pool not found");
            if (p.Status == TipPool.StatusDistributed) return new BaseResponse<HrTipPoolDto>(false, "validation", "A distributed pool is frozen");
            if (dto.Date.HasValue) p.Date = dto.Date.Value;
            if (dto.TotalAmount.HasValue)
            {
                if (dto.TotalAmount < 0) return new BaseResponse<HrTipPoolDto>(false, "validation", "Total must not be negative");
                p.TotalAmount = dto.TotalAmount.Value;
            }
            if (dto.Method != null)
            {
                if (!Methods.Contains(dto.Method)) return new BaseResponse<HrTipPoolDto>(false, "validation", "Method must be Equal, ByHours or ByPoints");
                p.Method = dto.Method;
            }
            if (dto.Notes != null) p.Notes = dto.Notes;
            if (dto.Participants != null)
            {
                var err = await SetParticipantsAsync(p, dto.Participants, ct);
                if (err != null) return new BaseResponse<HrTipPoolDto>(false, "validation", err);
            }
            await ComputeInternalAsync(p, ct);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<HrTipPoolDto>(true, null, "Updated", await ToDtoAsync(p, ct));
        }

        private async Task<string?> SetParticipantsAsync(TipPool p, IReadOnlyList<HrTipParticipantDto>? participants, CancellationToken ct)
        {
            var list = (participants ?? Array.Empty<HrTipParticipantDto>()).GroupBy(x => x.EmployeeId).Select(g => g.First()).ToList();
            var ids = list.Select(x => x.EmployeeId).ToList();
            var known = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).Select(e => e.Id).ToListAsync(ct);
            if (known.Count != ids.Count) return "One or more employees were not found";
            if (list.Any(x => x.Points is < 0)) return "Points must not be negative";

            var keep = p.Allocations.Where(a => ids.Contains(a.EmployeeId)).ToList();
            foreach (var gone in p.Allocations.Except(keep).ToList()) { p.Allocations.Remove(gone); _db.Remove(gone); }
            foreach (var x in list)
            {
                var a = keep.FirstOrDefault(k => k.EmployeeId == x.EmployeeId);
                if (a is null) { a = new TipPoolAllocation { EmployeeId = x.EmployeeId }; p.Allocations.Add(a); }
                a.Points = x.Points;
            }
            return null;
        }

        /// <summary>Recomputes Amount (and Hours for ByHours) on the pool's allocations in memory.</summary>
        private async Task ComputeInternalAsync(TipPool p, CancellationToken ct)
        {
            var allocs = p.Allocations.ToList();
            if (allocs.Count == 0) return;

            if (p.Method == TipPool.MethodByHours)
            {
                var start = DateTime.SpecifyKind(p.Date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                var end = start.AddDays(1);
                var ids = allocs.Select(a => a.EmployeeId).ToList();
                var minutes = await _db.Set<AttendanceRecord>().AsNoTracking()
                    .Where(r => ids.Contains(r.EmployeeId) && r.ClockIn >= start && r.ClockIn < end && r.WorkedMinutes != null)
                    .GroupBy(r => r.EmployeeId)
                    .Select(g => new { EmployeeId = g.Key, Minutes = g.Sum(r => r.WorkedMinutes!.Value) })
                    .ToListAsync(ct);
                foreach (var a in allocs)
                    a.Hours = Math.Round((minutes.FirstOrDefault(m => m.EmployeeId == a.EmployeeId)?.Minutes ?? 0) / 60m, 2);
            }

            var weights = allocs.Select(a => (a.EmployeeId, Weight: p.Method switch
            {
                TipPool.MethodByHours => a.Hours ?? 0m,
                TipPool.MethodByPoints => a.Points ?? 0m,
                _ => 1m,
            })).ToList();
            var shares = HrCalculations.Distribute(p.TotalAmount, weights);
            foreach (var a in allocs)
                a.Amount = shares.First(s => s.EmployeeId == a.EmployeeId).Amount;
        }

        public async Task<BaseResponse<HrTipPoolDto>> ComputeAsync(int id, CancellationToken ct = default)
        {
            var p = await Pools().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return new BaseResponse<HrTipPoolDto>(false, "not_found", "Pool not found");
            if (p.Status == TipPool.StatusDistributed) return new BaseResponse<HrTipPoolDto>(false, "validation", "A distributed pool is frozen");
            await ComputeInternalAsync(p, ct);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<HrTipPoolDto>(true, null, "Computed", await ToDtoAsync(p, ct));
        }

        public async Task<BaseResponse<HrTipPoolDto>> DistributeAsync(int id, CancellationToken ct = default)
        {
            var p = await Pools().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return new BaseResponse<HrTipPoolDto>(false, "not_found", "Pool not found");
            if (p.Status == TipPool.StatusDistributed) return new BaseResponse<HrTipPoolDto>(false, "validation", "Already distributed");
            if (p.Allocations.Count == 0) return new BaseResponse<HrTipPoolDto>(false, "validation", "Add at least one employee first");
            await ComputeInternalAsync(p, ct);
            p.Status = TipPool.StatusDistributed;
            p.DistributedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _log.LogInformation("HR tip pool {Id} distributed ({Total}) by {User}", p.Id, p.TotalAmount, _user.UserName);
            return new BaseResponse<HrTipPoolDto>(true, null, "Distributed", await ToDtoAsync(p, ct));
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var p = await Pools().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null || p.Status == TipPool.StatusDistributed) return false;
            _db.Remove(p);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<HrTipReportDto> ReportAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
        {
            var pools = await Pools().AsNoTracking()
                .Where(p => p.Date >= from && p.Date <= to && p.Status == TipPool.StatusDistributed).ToListAsync(ct);
            var ids = pools.SelectMany(p => p.Allocations).Select(a => a.EmployeeId).Distinct().ToList();
            var names = await _db.Set<Employee>().AsNoTracking().Where(e => ids.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
            var byDay = pools.GroupBy(p => p.Date).OrderBy(g => g.Key)
                .Select(g => new HrTipReportDayDto(g.Key, g.Count(), g.Sum(p => p.TotalAmount))).ToList();
            var byEmp = pools.SelectMany(p => p.Allocations).GroupBy(a => a.EmployeeId)
                .Select(g => new HrTipReportEmployeeDto(g.Key, names.GetValueOrDefault(g.Key, ""), g.Sum(a => a.Amount)))
                .OrderByDescending(x => x.Amount).ToList();
            return new HrTipReportDto(from, to, pools.Sum(p => p.TotalAmount), byDay, byEmp);
        }
    }
}
