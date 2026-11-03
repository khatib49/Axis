using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Branches
{
    public class BranchService : IBranchService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<BranchService> _logger;

        public BranchService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<BranchService> logger)
        {
            _db = db;
            _user = user;
            _logger = logger;
        }

        // ── Mapping helpers ─────────────────────────────────────────────
        internal static BranchDto ToDto(Branch b) => new(
            b.Id, b.Code, b.Name, b.Address, b.Phone, b.CurrencyCode, b.TimeZoneId, b.IsActive, b.IsDefault,
            FormatTime(b.OpeningTime), FormatTime(b.ClosingTime), b.CreatedOn, b.ModifiedOn);

        private static string? FormatTime(TimeSpan? t) => t.HasValue ? t.Value.ToString(@"hh\:mm") : null;

        private static bool TryParseTime(string? raw, out TimeSpan? value, out string? error)
        {
            value = null; error = null;
            if (string.IsNullOrWhiteSpace(raw)) return true;
            if (TimeSpan.TryParse(raw.Trim(), out var t) && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1))
            {
                value = t;
                return true;
            }
            error = $"'{raw}' is not a valid time (use HH:mm).";
            return false;
        }

        // ── CRUD ─────────────────────────────────────────────────────────
        public async Task<IReadOnlyList<BranchDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
        {
            var q = _db.Set<Branch>().AsNoTracking();
            if (!includeInactive) q = q.Where(b => b.IsActive);
            var list = await q.OrderByDescending(b => b.IsDefault).ThenBy(b => b.Name).ToListAsync(ct);
            return list.Select(ToDto).ToList();
        }

        public async Task<BranchDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var b = await _db.Set<Branch>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return b is null ? null : ToDto(b);
        }

        public async Task<BaseResponse<BranchDto>> CreateAsync(BranchCreateDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Code)) return new(false, "Code is required.", "Validation failed");
            if (string.IsNullOrWhiteSpace(dto.Name)) return new(false, "Name is required.", "Validation failed");
            var code = dto.Code.Trim().ToUpperInvariant();
            if (await _db.Set<Branch>().AnyAsync(b => b.Code == code, ct))
                return new(false, $"A branch with code '{code}' already exists.", "Validation failed");
            if (!TryParseTime(dto.OpeningTime, out var open, out var err) || !TryParseTime(dto.ClosingTime, out var close, out err))
                return new(false, err, "Validation failed");

            var isFirst = !await _db.Set<Branch>().AnyAsync(ct);
            var e = new Branch
            {
                Code = code,
                Name = dto.Name.Trim(),
                Address = dto.Address?.Trim(),
                Phone = dto.Phone?.Trim(),
                CurrencyCode = string.IsNullOrWhiteSpace(dto.CurrencyCode) ? "USD" : dto.CurrencyCode.Trim().ToUpperInvariant(),
                TimeZoneId = string.IsNullOrWhiteSpace(dto.TimeZoneId) ? "Asia/Beirut" : dto.TimeZoneId.Trim(),
                IsActive = dto.IsActive,
                IsDefault = dto.IsDefault || isFirst,
                OpeningTime = open,
                ClosingTime = close,
            };
            if (e.IsDefault) await ClearDefaultsAsync(ct);
            _db.Add(e);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Branch {Code} created by {User}", e.Code, _user.UserName);
            return new(true, null, "Created", ToDto(e));
        }

        public async Task<BaseResponse<BranchDto>> UpdateAsync(int id, BranchUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _db.Set<Branch>().FirstOrDefaultAsync(b => b.Id == id, ct);
            if (e is null) return new(false, "Branch not found.", "Not found");

            if (dto.Code is not null)
            {
                var code = dto.Code.Trim().ToUpperInvariant();
                if (code.Length == 0) return new(false, "Code is required.", "Validation failed");
                if (await _db.Set<Branch>().AnyAsync(b => b.Code == code && b.Id != id, ct))
                    return new(false, $"A branch with code '{code}' already exists.", "Validation failed");
                e.Code = code;
            }
            if (dto.OpeningTime is not null)
            {
                if (!TryParseTime(dto.OpeningTime, out var open, out var err)) return new(false, err, "Validation failed");
                e.OpeningTime = open;
            }
            if (dto.ClosingTime is not null)
            {
                if (!TryParseTime(dto.ClosingTime, out var close, out var err)) return new(false, err, "Validation failed");
                e.ClosingTime = close;
            }
            if (dto.Name is not null && dto.Name.Trim().Length > 0) e.Name = dto.Name.Trim();
            if (dto.Address is not null) e.Address = dto.Address.Trim();
            if (dto.Phone is not null) e.Phone = dto.Phone.Trim();
            if (!string.IsNullOrWhiteSpace(dto.CurrencyCode)) e.CurrencyCode = dto.CurrencyCode.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(dto.TimeZoneId)) e.TimeZoneId = dto.TimeZoneId.Trim();
            if (dto.IsActive.HasValue)
            {
                if (!dto.IsActive.Value && e.IsDefault) return new(false, "The default branch cannot be deactivated.", "Validation failed");
                e.IsActive = dto.IsActive.Value;
            }
            if (dto.IsDefault == true && !e.IsDefault)
            {
                await ClearDefaultsAsync(ct);
                e.IsDefault = true;
                e.IsActive = true;
            }
            else if (dto.IsDefault == false && e.IsDefault)
            {
                return new(false, "Mark another branch as default instead of clearing this one.", "Validation failed");
            }
            e.ModifiedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Updated", ToDto(e));
        }

        public async Task<BaseResponse<bool>> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<Branch>().FirstOrDefaultAsync(b => b.Id == id, ct);
            if (e is null) return new(false, "Branch not found.", "Not found", false);
            if (e.IsDefault && await _db.Set<Branch>().CountAsync(ct) > 1)
                return new(false, "The default branch cannot be deleted while other branches exist.", "Validation failed", false);
            _db.Remove(e);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Deleted", true);
        }

        private async Task ClearDefaultsAsync(CancellationToken ct)
        {
            var defaults = await _db.Set<Branch>().Where(b => b.IsDefault).ToListAsync(ct);
            foreach (var d in defaults) d.IsDefault = false;
        }

        // ── /me ──────────────────────────────────────────────────────────
        public async Task<MyBranchesDto> GetMineAsync(CancellationToken ct = default)
        {
            var all = await _db.Set<Branch>().AsNoTracking()
                .Where(b => b.IsActive)
                .OrderByDescending(b => b.IsDefault).ThenBy(b => b.Name)
                .ToListAsync(ct);
            var globalDefault = all.FirstOrDefault(b => b.IsDefault) ?? all.FirstOrDefault();

            if (_user.IsInRole("admin"))
                return new(all.Select(ToDto).ToList(), globalDefault?.Id, "admin");

            var userId = _user.UserId;
            if (userId.HasValue)
            {
                var assignments = await _db.Set<UserBranch>().AsNoTracking()
                    .Where(ub => ub.UserId == userId.Value)
                    .ToListAsync(ct);
                var assignedIds = assignments.Select(a => a.BranchId).ToHashSet();
                var mine = all.Where(b => assignedIds.Contains(b.Id)).ToList();
                if (mine.Count > 0)
                {
                    var userDefault = assignments.FirstOrDefault(a => a.IsDefault && assignedIds.Contains(a.BranchId))?.BranchId
                                      ?? mine.First().Id;
                    return new(mine.Select(ToDto).ToList(), userDefault, "assigned");
                }
            }

            var fallback = globalDefault is null ? new List<BranchDto>() : new List<BranchDto> { ToDto(globalDefault) };
            return new(fallback, globalDefault?.Id, "default-fallback");
        }

        // ── User assignment ─────────────────────────────────────────────
        public async Task<IReadOnlyList<UserBranchDto>> ListUsersAsync(int branchId, CancellationToken ct = default)
        {
            var rows = await (
                from ub in _db.Set<UserBranch>().AsNoTracking()
                join b in _db.Set<Branch>().AsNoTracking() on ub.BranchId equals b.Id
                join u in _db.Users.AsNoTracking() on ub.UserId equals u.Id into uj
                from u in uj.DefaultIfEmpty()
                where ub.BranchId == branchId
                select new UserBranchDto(ub.Id, ub.UserId, u != null ? (u.DisplayName ?? u.UserName) : null, u != null ? u.Email : null,
                    b.Id, b.Code, b.Name, ub.IsDefault)
            ).ToListAsync(ct);
            return rows;
        }

        public async Task<BaseResponse<UserBranchDto>> AssignUserAsync(int branchId, AssignUserRequest req, CancellationToken ct = default)
        {
            var branch = await _db.Set<Branch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId, ct);
            if (branch is null) return new(false, "Branch not found.", "Not found");
            if (!await _db.Users.AnyAsync(u => u.Id == req.UserId, ct)) return new(false, "User not found.", "Not found");

            var existing = await _db.Set<UserBranch>().FirstOrDefaultAsync(x => x.BranchId == branchId && x.UserId == req.UserId, ct);
            if (existing is null)
            {
                existing = new UserBranch { BranchId = branchId, UserId = req.UserId };
                _db.Add(existing);
            }
            if (req.IsDefault)
            {
                var others = await _db.Set<UserBranch>().Where(x => x.UserId == req.UserId && x.IsDefault && x.BranchId != branchId).ToListAsync(ct);
                foreach (var o in others) o.IsDefault = false;
            }
            existing.IsDefault = req.IsDefault;
            await _db.SaveChangesAsync(ct);

            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == req.UserId, ct);
            return new(true, null, "Assigned", new UserBranchDto(existing.Id, existing.UserId, user?.DisplayName ?? user?.UserName, user?.Email,
                branch.Id, branch.Code, branch.Name, existing.IsDefault));
        }

        public async Task<BaseResponse<bool>> UnassignUserAsync(int branchId, int userId, CancellationToken ct = default)
        {
            var existing = await _db.Set<UserBranch>().FirstOrDefaultAsync(x => x.BranchId == branchId && x.UserId == userId, ct);
            if (existing is null) return new(false, "Assignment not found.", "Not found", false);
            _db.Remove(existing);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Removed", true);
        }

        // ── Settings ─────────────────────────────────────────────────────
        public async Task<IReadOnlyList<BranchSettingDto>> GetSettingsAsync(int branchId, CancellationToken ct = default)
        {
            return await _db.Set<BranchSetting>().AsNoTracking()
                .Where(s => s.BranchId == branchId)
                .OrderBy(s => s.Key)
                .Select(s => new BranchSettingDto(s.Key, s.Value, s.UpdatedOn, s.UpdatedBy))
                .ToListAsync(ct);
        }

        public async Task<BaseResponse<IReadOnlyList<BranchSettingDto>>> SetSettingsAsync(int branchId, IEnumerable<BranchSettingUpsertDto> settings, CancellationToken ct = default)
        {
            if (!await _db.Set<Branch>().AnyAsync(b => b.Id == branchId, ct))
                return new(false, "Branch not found.", "Not found");

            var incoming = settings.Where(s => !string.IsNullOrWhiteSpace(s.Key)).ToList();
            var keys = incoming.Select(s => s.Key.Trim()).ToList();
            var existing = await _db.Set<BranchSetting>().Where(s => s.BranchId == branchId && keys.Contains(s.Key)).ToListAsync(ct);
            var now = DateTime.UtcNow;
            foreach (var s in incoming)
            {
                var key = s.Key.Trim();
                var row = existing.FirstOrDefault(x => x.Key == key);
                if (s.Value is null)
                {
                    if (row is not null) _db.Remove(row);
                    continue;
                }
                if (row is null)
                {
                    row = new BranchSetting { BranchId = branchId, Key = key };
                    _db.Add(row);
                }
                row.Value = s.Value;
                row.UpdatedOn = now;
                row.UpdatedBy = _user.UserName;
            }
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Saved", await GetSettingsAsync(branchId, ct));
        }

        // ── Consolidated dashboard ───────────────────────────────────────
        /// <summary>One row of facts per branch key. BranchId null = legacy data without a branch.</summary>
        private sealed record BranchFact(int? BranchId, decimal Sales, int Orders, int OpenInvoices, decimal OpenInvoicesValue);

        /// <summary>
        /// The only place that knows how transactions map to branches. TransactionRecord
        /// has no BranchId today, so every fact comes back with BranchId = null and is
        /// attributed to the default branch. When transactions get a BranchId, change the
        /// GroupBy key to t.BranchId here and nothing else.
        /// </summary>
        private async Task<List<BranchFact>> LoadFactsAsync(DateTime from, DateTime toExclusive, CancellationToken ct)
        {
            var tx = _db.Set<TransactionRecord>().AsNoTracking();

            var paid = await tx
                .Where(t => t.StatusId == 6 && (t.PaidOn ?? t.CreatedOn) >= from && (t.PaidOn ?? t.CreatedOn) < toExclusive)
                .Select(t => new { Key = (int?)null, t.TotalPrice })
                .ToListAsync(ct);
            var open = await tx
                .Where(t => t.StatusId == 7)
                .Select(t => new { Key = (int?)null, t.TotalPrice })
                .ToListAsync(ct);

            var keys = paid.Select(p => p.Key).Concat(open.Select(o => o.Key)).Distinct().ToList();
            return keys.Select(k => new BranchFact(
                k,
                paid.Where(p => p.Key == k).Sum(p => p.TotalPrice),
                paid.Count(p => p.Key == k),
                open.Count(o => o.Key == k),
                open.Where(o => o.Key == k).Sum(o => o.TotalPrice))).ToList();
        }

        public async Task<ConsolidatedDashboardDto> GetConsolidatedAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var today = BusinessDate.ToDay(DateTime.UtcNow);
            var toDay = to.HasValue ? BusinessDate.ToDay(to.Value) : today;
            var fromDay = from.HasValue ? BusinessDate.ToDay(from.Value) : toDay.AddDays(-6);
            if (fromDay > toDay) (fromDay, toDay) = (toDay, fromDay);
            var toExclusive = toDay.AddDays(1);

            var branches = await _db.Set<Branch>().AsNoTracking()
                .OrderByDescending(b => b.IsDefault).ThenBy(b => b.Name).ToListAsync(ct);
            var facts = await LoadFactsAsync(fromDay, toExclusive, ct);

            var defaultBranch = branches.FirstOrDefault(b => b.IsDefault) ?? branches.FirstOrDefault();
            var legacy = facts.Any(f => f.BranchId is null);

            var rows = branches.Select(b =>
            {
                var mine = facts.Where(f => f.BranchId == b.Id || (f.BranchId is null && defaultBranch?.Id == b.Id)).ToList();
                var sales = mine.Sum(f => f.Sales);
                var orders = mine.Sum(f => f.Orders);
                return new BranchKpiDto(b.Id, b.Code, b.Name, b.CurrencyCode, b.IsDefault,
                    sales, orders, orders == 0 ? 0 : Math.Round(sales / orders, 2),
                    mine.Sum(f => f.OpenInvoices), mine.Sum(f => f.OpenInvoicesValue));
            }).ToList();

            var tSales = facts.Sum(f => f.Sales);
            var tOrders = facts.Sum(f => f.Orders);
            var total = new BranchKpiDto(0, "ALL", "All branches", defaultBranch?.CurrencyCode ?? "USD", false,
                tSales, tOrders, tOrders == 0 ? 0 : Math.Round(tSales / tOrders, 2),
                facts.Sum(f => f.OpenInvoices), facts.Sum(f => f.OpenInvoicesValue));

            var attribution = legacy ? "default-branch" : "branch";
            var note = legacy
                ? (defaultBranch is null
                    ? "Transactions carry no branch yet and no branch exists; totals are unattributed."
                    : $"Transactions carry no branch yet; all totals are attributed to the default branch '{defaultBranch.Name}'.")
                : "Totals are grouped by the branch recorded on each transaction.";
            return new ConsolidatedDashboardDto(fromDay, toDay, attribution, note, rows, total);
        }
    }
}
