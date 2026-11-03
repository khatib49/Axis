using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.AccountingPlus
{
    public class AccountingPeriodService : IAccountingPeriodService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<AccountingPeriodService> _logger;

        public AccountingPeriodService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<AccountingPeriodService> logger)
        {
            _db = db;
            _user = user;
            _logger = logger;
        }

        public async Task<BaseResponse> EnsureOpenAsync(DateTime date, CancellationToken ct = default)
        {
            var d = AcctDates.Utc(date);
            var locked = await _db.Set<AccountingPeriod>().AsNoTracking()
                .AnyAsync(p => p.Year == d.Year && p.Month == d.Month && p.IsLocked, ct);
            return locked
                ? new BaseResponse(false, $"Accounting period {d:yyyy-MM} is locked")
                : new BaseResponse(true, null);
        }

        public async Task<List<AccountingPeriodDto>> ListAsync(int year, CancellationToken ct = default)
        {
            var rows = await _db.Set<AccountingPeriod>().AsNoTracking().Where(p => p.Year == year).ToListAsync(ct);
            return Enumerable.Range(1, 12).Select(m =>
            {
                var p = rows.FirstOrDefault(r => r.Month == m);
                return p is null
                    ? new AccountingPeriodDto(year, m, false, null, null, null)
                    : new AccountingPeriodDto(p.Year, p.Month, p.IsLocked, p.LockedOn, p.LockedBy, p.Note);
            }).ToList();
        }

        public async Task<BaseResponse<AccountingPeriodDto>> LockAsync(PeriodLockRequestDto dto, CancellationToken ct = default)
        {
            if (dto.Month < 1 || dto.Month > 12 || dto.Year < 2000 || dto.Year > 2100)
                return new BaseResponse<AccountingPeriodDto>(false, "Invalid year/month", "");

            var p = await _db.Set<AccountingPeriod>().FirstOrDefaultAsync(x => x.Year == dto.Year && x.Month == dto.Month, ct);
            if (p is null)
            {
                p = new AccountingPeriod { Year = dto.Year, Month = dto.Month };
                _db.Add(p);
            }
            p.IsLocked = true;
            p.LockedOn = DateTime.UtcNow;
            p.LockedBy = _user.UserName;
            p.Note = dto.Note;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Accounting period {Year}-{Month} locked by {User}", p.Year, p.Month, p.LockedBy);
            return new BaseResponse<AccountingPeriodDto>(true, null, "Period locked", ToDto(p));
        }

        public async Task<BaseResponse<AccountingPeriodDto>> UnlockAsync(int year, int month, CancellationToken ct = default)
        {
            var p = await _db.Set<AccountingPeriod>().FirstOrDefaultAsync(x => x.Year == year && x.Month == month, ct);
            if (p is null || !p.IsLocked)
                return new BaseResponse<AccountingPeriodDto>(true, null, "Period already open", new AccountingPeriodDto(year, month, false, null, null, p?.Note));
            p.IsLocked = false;
            p.LockedOn = null;
            p.LockedBy = null;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Accounting period {Year}-{Month} unlocked by {User}", year, month, _user.UserName);
            return new BaseResponse<AccountingPeriodDto>(true, null, "Period unlocked", ToDto(p));
        }

        private static AccountingPeriodDto ToDto(AccountingPeriod p) => new(p.Year, p.Month, p.IsLocked, p.LockedOn, p.LockedBy, p.Note);
    }
}
