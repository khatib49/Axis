using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Promotions
{
    public class HappyHourService : IHappyHourService
    {
        private readonly ApplicationDbContext _db;
        private readonly IPromotionInvoiceAdjuster _adjuster;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<HappyHourService> _logger;
        private readonly TimeZoneInfo _tz = HappyHourEngine.VenueTimeZone();

        public HappyHourService(ApplicationDbContext db, IPromotionInvoiceAdjuster adjuster,
            ICurrentUserAccessor user, ILogger<HappyHourService> logger)
        {
            _db = db;
            _adjuster = adjuster;
            _user = user;
            _logger = logger;
        }

        private static HappyHourRuleDto ToDto(HappyHourRule r) => new(
            r.Id, r.Name, r.DaysOfWeekCsv, r.StartTime.ToString("HH:mm"), r.EndTime.ToString("HH:mm"),
            r.DiscountPercent, r.CategoryIdsCsv, r.ItemIdsCsv, r.IsActive, r.Priority, r.BranchId, r.CreatedOn);

        private static string? Validate(HappyHourRuleUpsertDto dto, out TimeOnly start, out TimeOnly end)
        {
            start = end = default;
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required.";
            if (!HappyHourEngine.TryParseTime(dto.StartTime, out start)) return "Start time must be HH:mm.";
            if (!HappyHourEngine.TryParseTime(dto.EndTime, out end)) return "End time must be HH:mm.";
            if (dto.DiscountPercent <= 0 || dto.DiscountPercent > 100) return "Discount percent must be between 0 and 100.";
            var days = PromoCsv.ParseInts(dto.DaysOfWeekCsv);
            if (days.Any(d => d < 1 || d > 7)) return "Days of week must be 1 (Monday) to 7 (Sunday).";
            return null;
        }

        private static void Apply(HappyHourRule r, HappyHourRuleUpsertDto dto, TimeOnly start, TimeOnly end)
        {
            r.Name = dto.Name.Trim();
            r.DaysOfWeekCsv = PromoCsv.Normalise(dto.DaysOfWeekCsv) ?? "";
            r.StartTime = start;
            r.EndTime = end;
            r.DiscountPercent = Math.Round(dto.DiscountPercent, 2);
            r.CategoryIdsCsv = PromoCsv.Normalise(dto.CategoryIdsCsv);
            r.ItemIdsCsv = PromoCsv.Normalise(dto.ItemIdsCsv);
            r.IsActive = dto.IsActive;
            r.Priority = dto.Priority;
        }

        // ── admin CRUD ───────────────────────────────────────────────────
        public async Task<List<HappyHourRuleDto>> ListAsync(bool includeInactive = true, CancellationToken ct = default)
        {
            var q = _db.Set<HappyHourRule>().AsNoTracking().AsQueryable();
            if (!includeInactive) q = q.Where(r => r.IsActive);
            var rows = await q.OrderByDescending(r => r.Priority).ThenBy(r => r.Name).ToListAsync(ct);
            return rows.Select(ToDto).ToList();
        }

        public async Task<HappyHourRuleDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<HappyHourRule>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return r is null ? null : ToDto(r);
        }

        public async Task<BaseResponse<HappyHourRuleDto>> CreateAsync(HappyHourRuleUpsertDto dto, CancellationToken ct = default)
        {
            var err = Validate(dto, out var start, out var end);
            if (err != null) return new BaseResponse<HappyHourRuleDto>(false, "Validation", err);
            var r = new HappyHourRule { BranchId = _user.BranchId, CreatedOn = DateTime.UtcNow };
            Apply(r, dto, start, end);
            _db.Add(r);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<HappyHourRuleDto>(true, null, "Rule created", ToDto(r));
        }

        public async Task<BaseResponse<HappyHourRuleDto>> UpdateAsync(int id, HappyHourRuleUpsertDto dto, CancellationToken ct = default)
        {
            var err = Validate(dto, out var start, out var end);
            if (err != null) return new BaseResponse<HappyHourRuleDto>(false, "Validation", err);
            var r = await _db.Set<HappyHourRule>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return new BaseResponse<HappyHourRuleDto>(false, "Not found", "Rule not found.");
            Apply(r, dto, start, end);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<HappyHourRuleDto>(true, null, "Rule updated", ToDto(r));
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<HappyHourRule>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return false;
            // Applications reference the rule (Restrict), so a used rule is deactivated instead.
            if (await _db.Set<HappyHourApplication>().AnyAsync(a => a.RuleId == id, ct))
                r.IsActive = false;
            else
                _db.Remove(r);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // ── till ─────────────────────────────────────────────────────────
        private async Task<(DateTime Utc, DateTime Local, List<HappyHourRule> Active)> ActiveRulesAsync(DateTime? atUtc, CancellationToken ct)
        {
            var utc = atUtc.HasValue ? DateTime.SpecifyKind(atUtc.Value.ToUniversalTime(), DateTimeKind.Utc) : DateTime.UtcNow;
            var local = HappyHourEngine.ToLocal(utc, _tz);
            var rules = await _db.Set<HappyHourRule>().AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);
            var active = rules.Where(r => HappyHourEngine.IsActiveAt(r, local))
                              .OrderByDescending(r => r.Priority).ThenByDescending(r => r.DiscountPercent).ToList();
            return (utc, local, active);
        }

        public async Task<HappyHourActiveNowDto> ActiveNowAsync(DateTime? atUtc = null, CancellationToken ct = default)
        {
            var (utc, local, active) = await ActiveRulesAsync(atUtc, ct);
            return new HappyHourActiveNowDto(utc, local, _tz.Id, active.Select(ToDto).ToList());
        }

        public async Task<BaseResponse<HappyHourPricePreviewDto>> PricePreviewAsync(int itemId, DateTime? atUtc = null, CancellationToken ct = default)
        {
            var item = await _db.Set<Item>().AsNoTracking()
                .Where(i => i.Id == itemId)
                .Select(i => new { i.Id, i.Name, i.Price, i.CategoryId })
                .FirstOrDefaultAsync(ct);
            if (item is null) return new BaseResponse<HappyHourPricePreviewDto>(false, "Not found", "Item not found.");

            var (_, _, active) = await ActiveRulesAsync(atUtc, ct);
            var rule = HappyHourEngine.BestFor(active, item.Id, item.CategoryId);
            var pct = rule?.DiscountPercent ?? 0m;
            return new BaseResponse<HappyHourPricePreviewDto>(true, null, rule is null ? "No happy hour right now" : "Happy hour price",
                new HappyHourPricePreviewDto(item.Id, item.Name, item.Price, pct,
                    HappyHourEngine.DiscountedPrice(item.Price, pct), rule?.Id, rule?.Name, rule != null));
        }

        public async Task<BaseResponse<HappyHourApplyResultDto>> ApplyAsync(HappyHourApplyDto dto, CancellationToken ct = default)
        {
            var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TransactionId, ct);
            if (trx is null) return new BaseResponse<HappyHourApplyResultDto>(false, "Invalid invoice", "The specified invoice does not exist.");
            if (trx.StatusId != PromotionInvoiceAdjuster.OpenInvoiceStatusId)
                return new BaseResponse<HappyHourApplyResultDto>(false, "Invoice not open", "Happy hour can only be applied to an open invoice.");

            var existing = await _db.Set<HappyHourApplication>().AsNoTracking().Include(a => a.Rule)
                .FirstOrDefaultAsync(a => a.TransactionId == trx.Id, ct);
            if (existing != null)
                return new BaseResponse<HappyHourApplyResultDto>(false, "Already applied", "Happy hour was already applied to this invoice.",
                    new HappyHourApplyResultDto(trx.Id, existing.RuleId, existing.Rule?.Name ?? "", existing.Amount, trx.TotalPrice, trx.TotalPrice, 0, AlreadyApplied: true));

            var (_, _, active) = await ActiveRulesAsync(null, ct);
            if (active.Count == 0)
                return new BaseResponse<HappyHourApplyResultDto>(false, "No happy hour", "No happy hour rule is in effect right now.");

            var lines = await _db.Set<TransactionItem>().AsNoTracking()
                .Where(ti => ti.TransactionRecordId == trx.Id && !ti.IsIncluded)
                .Select(ti => new PromoLine(ti.ItemId, ti.Item.CategoryId, ti.Item.Price, ti.Quantity))
                .ToListAsync(ct);

            var (amount, matched, top) = HappyHourEngine.DiscountFor(active, lines);
            if (matched == 0 || amount <= 0 || top is null)
                return new BaseResponse<HappyHourApplyResultDto>(false, "Nothing to discount", "No line on this invoice is covered by the current happy hour.");

            amount = Math.Min(amount, trx.TotalPrice);
            if (amount <= 0)
                return new BaseResponse<HappyHourApplyResultDto>(false, "Nothing to discount", "The invoice has nothing left to discount.");

            var adj = await _adjuster.ApplyAsync(trx.Id, -amount, $"happyhour:tx{trx.Id}",
                $"Happy hour '{top.Name}' discount {amount:F2} over {matched} line(s)", ct);
            if (!adj.Success)
                return new BaseResponse<HappyHourApplyResultDto>(false, adj.AlreadyApplied ? "Already applied" : "Cannot apply", adj.Error ?? "Could not apply happy hour.");

            _db.Add(new HappyHourApplication
            {
                RuleId = top.Id,
                TransactionId = trx.Id,
                Amount = amount,
                On = DateTime.UtcNow,
                By = _user.UserName,
            });
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Happy hour rule {RuleId} applied to invoice {TxId}: {Amount}", top.Id, trx.Id, amount);
            return new BaseResponse<HappyHourApplyResultDto>(true, null, "Happy hour applied",
                new HappyHourApplyResultDto(trx.Id, top.Id, top.Name, amount, adj.OldTotal, adj.NewTotal, matched));
        }
    }
}
