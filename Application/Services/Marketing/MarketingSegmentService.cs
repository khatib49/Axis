using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Domain.Models;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.Marketing
{
    /// <summary>
    /// Segment CRUD + audience evaluation. Audience = active users in role "client"
    /// joined to their paid transactions (StatusId 6, PaidOn ?? CreatedOn) and the
    /// loyalty customer row that shares their phone number.
    /// </summary>
    public class MarketingSegmentService : IMarketingSegmentService
    {
        public const int PreviewRows = 50;
        private const int PaidStatus = 6;

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;

        public MarketingSegmentService(ApplicationDbContext db, ICurrentUserAccessor user) { _db = db; _user = user; }

        internal static SegmentDto ToDto(MktSegment s) => new(s.Id, s.Name, SegmentRules.Parse(s.RulesJson), s.IsActive, s.CreatedOn, s.CreatedBy);

        public async Task<IReadOnlyList<SegmentDto>> ListAsync(CancellationToken ct = default) =>
            (await _db.Set<MktSegment>().AsNoTracking().OrderBy(s => s.Name).ToListAsync(ct)).Select(ToDto).ToList();

        public async Task<SegmentDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var s = await _db.Set<MktSegment>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return s is null ? null : ToDto(s);
        }

        public async Task<BaseResponse<SegmentDto>> CreateAsync(SegmentCreateDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return new(false, "Name is required.", "Validation failed");
            var e = new MktSegment
            {
                Name = dto.Name.Trim(), RulesJson = SegmentRules.Serialize(SegmentRules.Clean(dto.Rules)),
                IsActive = dto.IsActive, CreatedBy = _user.UserName, CreatedOn = DateTime.UtcNow,
            };
            _db.Add(e);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Created", ToDto(e));
        }

        public async Task<BaseResponse<SegmentDto>> UpdateAsync(int id, SegmentUpdateDto dto, CancellationToken ct = default)
        {
            var e = await _db.Set<MktSegment>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return new(false, "Segment not found.", "Not found");
            if (dto.Name is not null && dto.Name.Trim().Length > 0) e.Name = dto.Name.Trim();
            if (dto.Rules is not null) e.RulesJson = SegmentRules.Serialize(SegmentRules.Clean(dto.Rules));
            if (dto.IsActive.HasValue) e.IsActive = dto.IsActive.Value;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Updated", ToDto(e));
        }

        public async Task<BaseResponse<bool>> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _db.Set<MktSegment>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return new(false, "Segment not found.", "Not found", false);
            if (await _db.Set<MktCampaign>().AnyAsync(c => c.SegmentId == id, ct))
                return new(false, "Segment is used by a campaign; deactivate it instead.", "In use", false);
            _db.Remove(e);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Deleted", true);
        }

        public async Task<BaseResponse<AudiencePreviewDto>> PreviewAsync(int id, CancellationToken ct = default)
        {
            var s = await _db.Set<MktSegment>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (s is null) return new(false, "Segment not found.", "Not found");
            return new(true, null, "OK", await PreviewRulesAsync(SegmentRules.Parse(s.RulesJson), ct));
        }

        public async Task<AudiencePreviewDto> PreviewRulesAsync(SegmentRulesDto rules, CancellationToken ct = default)
        {
            var all = await BuildAudienceAsync(rules, ct);
            return new AudiencePreviewDto(all.Count, all.Take(PreviewRows).ToList());
        }

        public async Task<IReadOnlyList<AudienceRowDto>> BuildAudienceAsync(SegmentRulesDto rules, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var facts = await LoadFactsAsync(SegmentRules.Clean(rules), now, ct);
            return facts.Where(f => SegmentRules.Matches(rules, f, now))
                .OrderByDescending(f => f.LastVisit ?? DateTime.MinValue).ThenBy(f => f.Name)
                .Select(f => new AudienceRowDto(f.UserId, f.Name, f.Phone, f.Email, f.LastVisit,
                    rules.SpendPeriodDays is > 0 ? f.SpendInPeriod : f.SpendAll))
                .ToList();
        }

        /// <summary>Loads every client user with their sales / loyalty facts (rules only affect the branch filter and the spend window).</summary>
        private async Task<List<AudienceFacts>> LoadFactsAsync(SegmentRulesDto rules, DateTime now, CancellationToken ct)
        {
            var clientIds = await (
                from ur in _db.UserRoles
                join r in _db.Roles on ur.RoleId equals r.Id
                where r.NormalizedName == "CLIENT" || r.Name == "client"
                select ur.UserId).Distinct().ToListAsync(ct);
            if (clientIds.Count == 0) return new List<AudienceFacts>();
            var idSet = clientIds.ToHashSet();

            var users = await _db.Users.AsNoTracking()
                .Where(u => u.DeletedAt == null && u.StatusId != (int)Domain.Identity.UserStatus.Deleted)
                .Select(u => new { u.Id, u.DisplayName, u.FirstName, u.LastName, u.UserName, u.Email, u.PhoneNumber })
                .ToListAsync(ct);
            users = users.Where(u => idSet.Contains(u.Id)).ToList();
            if (users.Count == 0) return new List<AudienceFacts>();

            var txQuery = _db.Set<TransactionRecord>().AsNoTracking().Where(t => t.StatusId == PaidStatus && t.UserId != null);
            if (rules.BranchId is int branch) txQuery = txQuery.Where(t => t.BranchId == branch);
            var tx = await txQuery.Select(t => new { UserId = t.UserId!.Value, When = t.PaidOn ?? t.CreatedOn, t.TotalPrice }).ToListAsync(ct);
            var periodStart = rules.SpendPeriodDays is int days ? now.AddDays(-days) : (DateTime?)null;
            var byUser = tx.GroupBy(t => t.UserId).ToDictionary(g => g.Key, g => new
            {
                Last = g.Max(t => (DateTime?)t.When),
                SpendAll = g.Sum(t => t.TotalPrice),
                VisitsAll = g.Count(),
                SpendPeriod = periodStart is null ? g.Sum(t => t.TotalPrice) : g.Where(t => t.When >= periodStart).Sum(t => t.TotalPrice),
                VisitsPeriod = periodStart is null ? g.Count() : g.Count(t => t.When >= periodStart),
            });

            var phones = users.Select(u => u.PhoneNumber).Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()).Distinct().ToList();
            var loyalty = phones.Count == 0
                ? new Dictionary<string, int>()
                : (await _db.Set<LoyaltyCustomer>().AsNoTracking().Where(l => phones.Contains(l.PhoneNumber))
                    .Select(l => new { l.PhoneNumber, l.TotalTicketsCurrentMonth }).ToListAsync(ct))
                    .GroupBy(l => l.PhoneNumber).ToDictionary(g => g.Key, g => g.Max(x => x.TotalTicketsCurrentMonth));

            var result = new List<AudienceFacts>(users.Count);
            foreach (var u in users)
            {
                byUser.TryGetValue(u.Id, out var s);
                var first = !string.IsNullOrWhiteSpace(u.FirstName) ? u.FirstName!.Trim()
                    : (u.DisplayName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                var name = !string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName!.Trim()
                    : string.Join(" ", new[] { u.FirstName, u.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
                if (name.Length == 0) name = u.Email ?? u.UserName ?? $"User {u.Id}";
                var phone = string.IsNullOrWhiteSpace(u.PhoneNumber) ? null : u.PhoneNumber.Trim();
                var tickets = phone is not null && loyalty.TryGetValue(phone, out var t) ? t : 0;
                result.Add(new AudienceFacts(u.Id, name, first, string.IsNullOrWhiteSpace(u.Email) ? null : u.Email.Trim(), phone,
                    s?.Last, s?.SpendAll ?? 0, s?.VisitsAll ?? 0, s?.SpendPeriod ?? 0, s?.VisitsPeriod ?? 0, tickets));
            }
            return result;
        }
    }
}
