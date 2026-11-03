using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Promotions
{
    /// <summary>
    /// Idempotent, re-evaluating promotion pass for an OPEN invoice.
    ///
    /// Happy hour: the discount over the current lines (using the rules in
    /// effect now) is compared with the amount stored on the invoice's
    /// HappyHourApplication row and only the difference is booked through the
    /// adjuster, with key happyhour:tx{id}:v{n} (n = row version). When no rule
    /// is in effect any more the discount booked earlier is KEPT (the lines were
    /// ordered during happy hour) — nothing is clawed back.
    ///
    /// Combos: for every active combo the number of whole sets on the invoice is
    /// counted (min over items of floor(lineQty / comboQty)); combos are
    /// allocated greedily, biggest saving first, so one line is never counted
    /// in two combos. The saving (sets × (parts − combo price)) is compared with
    /// the ComboApplication row and the difference booked with key
    /// combo:{comboId}:tx{id}:v{n}.
    ///
    /// Everything is committed with ONE SaveChangesAsync; the adjuster's guard
    /// (open invoice, never below zero, unique key) applies to each delta.
    /// </summary>
    public class PromotionAutoApplyService : IPromotionAutoApplyService
    {
        private readonly ApplicationDbContext _db;
        private readonly IPromotionInvoiceAdjuster _adjuster;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<PromotionAutoApplyService> _logger;
        private readonly TimeZoneInfo _tz = HappyHourEngine.VenueTimeZone();

        public PromotionAutoApplyService(ApplicationDbContext db, IPromotionInvoiceAdjuster adjuster,
            ICurrentUserAccessor user, ILogger<PromotionAutoApplyService> logger)
        {
            _db = db;
            _adjuster = adjuster;
            _user = user;
            _logger = logger;
        }

        public async Task<BaseResponse<AutoApplyResultDto>> ApplyAsync(int transactionId, string? actor = null, CancellationToken ct = default)
        {
            var by = string.IsNullOrWhiteSpace(actor) ? (_user.UserName ?? "system") : actor!.Trim();

            // Tracked: the adjuster mutates TotalPrice on this same instance.
            var trx = await _db.Set<TransactionRecord>().FirstOrDefaultAsync(t => t.Id == transactionId, ct);
            if (trx is null)
                return new BaseResponse<AutoApplyResultDto>(false, "Invalid invoice", "The specified invoice does not exist.");
            if (trx.StatusId != PromotionInvoiceAdjuster.OpenInvoiceStatusId)
                return new BaseResponse<AutoApplyResultDto>(false, "Invoice not open",
                    $"Invoice #{trx.Id} is not open; promotions are only applied to open invoices.");

            var lines = await _db.Set<TransactionItem>().AsNoTracking()
                .Where(ti => ti.TransactionRecordId == trx.Id && !ti.IsIncluded)
                .Select(ti => new PromoLine(ti.ItemId, ti.Item.CategoryId, ti.Item.Price, ti.Quantity))
                .ToListAsync(ct);

            var totalBefore = trx.TotalPrice;
            var messages = new List<string>();

            var happyHour = await EvaluateHappyHourAsync(trx, lines, by, messages, ct);
            var combos = await EvaluateCombosAsync(trx, lines, by, messages, ct);

            await _db.SaveChangesAsync(ct);

            var changed = (happyHour?.Delta ?? 0m) != 0m || combos.Any(c => c.Delta != 0m);
            if (changed)
                _logger.LogInformation("Auto-apply on invoice {TxId}: {Before} -> {After} ({Messages})",
                    trx.Id, totalBefore, trx.TotalPrice, string.Join("; ", messages));

            return new BaseResponse<AutoApplyResultDto>(true, null,
                changed ? "Promotions applied" : "Promotions are up to date",
                new AutoApplyResultDto(trx.Id, happyHour, combos, totalBefore, trx.TotalPrice, messages));
        }

        public async Task<BaseResponse<AppliedPromotionsDto>> GetAppliedAsync(int transactionId, CancellationToken ct = default)
        {
            var trx = await _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => t.Id == transactionId).Select(t => new { t.Id, t.StatusId, t.TotalPrice })
                .FirstOrDefaultAsync(ct);
            if (trx is null)
                return new BaseResponse<AppliedPromotionsDto>(false, "Invalid invoice", "The specified invoice does not exist.");

            var hh = await _db.Set<HappyHourApplication>().AsNoTracking().Include(a => a.Rule)
                .FirstOrDefaultAsync(a => a.TransactionId == trx.Id, ct);
            var combos = await _db.Set<ComboApplication>().AsNoTracking().Include(a => a.Combo)
                .Where(a => a.TransactionId == trx.Id && a.Amount > 0)
                .OrderBy(a => a.Id)
                .Select(a => new AutoApplyComboDto(a.ComboId, a.Combo.Name, a.Quantity, a.Amount, 0m))
                .ToListAsync(ct);
            var coupons = await _db.Set<CouponRedemption>().AsNoTracking()
                .Where(r => r.TransactionId == trx.Id)
                .OrderBy(r => r.Id)
                .Select(r => new AppliedCouponDto(r.CouponId, r.Coupon.Code, r.Coupon.Name, r.DiscountAmount))
                .ToListAsync(ct);

            var hhDto = hh is null || hh.Amount <= 0 ? null
                : new AutoApplyHappyHourDto(hh.RuleId, hh.Rule?.Name ?? "", hh.Amount, 0m, 0);
            var total = (hhDto?.Amount ?? 0m) + combos.Sum(c => c.Amount) + coupons.Sum(c => c.Amount);

            return new BaseResponse<AppliedPromotionsDto>(true, null, "OK",
                new AppliedPromotionsDto(trx.Id, trx.StatusId == PromotionInvoiceAdjuster.OpenInvoiceStatusId,
                    trx.TotalPrice, hhDto, combos, coupons, Math.Round(total, 2)));
        }

        // ── happy hour ─────────────────────────────────────────────────────
        private async Task<AutoApplyHappyHourDto?> EvaluateHappyHourAsync(
            TransactionRecord trx, List<PromoLine> lines, string by, List<string> messages, CancellationToken ct)
        {
            var app = await _db.Set<HappyHourApplication>().Include(a => a.Rule)
                .FirstOrDefaultAsync(a => a.TransactionId == trx.Id, ct);

            var rules = await _db.Set<HappyHourRule>().AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);
            var active = HappyHourEngine.ActiveRules(rules, HappyHourEngine.ToLocal(DateTime.UtcNow, _tz));

            if (active.Count == 0)
            {
                if (app is null || app.Amount <= 0) return null;
                messages.Add($"No happy hour is in effect now; the earlier '{app.Rule?.Name}' discount of {app.Amount:F2} is kept.");
                return new AutoApplyHappyHourDto(app.RuleId, app.Rule?.Name ?? "", app.Amount, 0m, 0);
            }

            var (desired, matched, top) = HappyHourEngine.DiscountFor(active, lines);
            if (top is null) desired = 0m;

            var stored = app?.Amount ?? 0m;
            var delta = Math.Round(stored - desired, 2); // negative = more discount, positive = refund
            if (delta < 0 && trx.TotalPrice + delta < 0)
            {
                delta = -trx.TotalPrice;                    // never below zero
                desired = Math.Round(stored - delta, 2);
            }

            if (delta == 0m)
                return app is null || app.Amount <= 0
                    ? null
                    : new AutoApplyHappyHourDto(app.RuleId, app.Rule?.Name ?? "", app.Amount, 0m, matched);

            var rule = top ?? app!.Rule;
            var version = (app?.Version ?? 0) + 1;
            var adj = await _adjuster.ApplyAsync(trx.Id, delta, $"happyhour:tx{trx.Id}:v{version}",
                $"Happy hour '{rule.Name}' re-evaluated: discount {stored:F2} -> {desired:F2} over {matched} line(s)", ct);
            if (!adj.Success)
            {
                messages.Add($"Happy hour could not be updated: {adj.Error}");
                return app is null ? null : new AutoApplyHappyHourDto(app.RuleId, app.Rule?.Name ?? "", app.Amount, 0m, matched);
            }

            if (app is null)
            {
                app = new HappyHourApplication { TransactionId = trx.Id };
                _db.Add(app);
            }
            app.RuleId = rule.Id;
            app.Amount = desired;
            app.Version = version;
            app.On = DateTime.UtcNow;
            app.By = by;

            messages.Add(delta < 0
                ? $"Happy hour '{rule.Name}': {-delta:F2} off (total discount {desired:F2})."
                : $"Happy hour '{rule.Name}': {delta:F2} restored (total discount {desired:F2}).");
            return new AutoApplyHappyHourDto(rule.Id, rule.Name, desired, delta, matched);
        }

        // ── combos ─────────────────────────────────────────────────────────
        private async Task<List<AutoApplyComboDto>> EvaluateCombosAsync(
            TransactionRecord trx, List<PromoLine> lines, string by, List<string> messages, CancellationToken ct)
        {
            var apps = await _db.Set<ComboApplication>().Include(a => a.Combo)
                .Where(a => a.TransactionId == trx.Id).ToListAsync(ct);
            var combos = await _db.Set<Combo>().AsNoTracking().Include(c => c.Items)
                .Where(c => c.IsActive).ToListAsync(ct);

            var available = lines.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
            var unitPrice = lines.GroupBy(l => l.ItemId).ToDictionary(g => g.Key, g => g.First().UnitPrice);

            // Candidate combos: every item present at least once; saving per set > 0.
            var candidates = combos
                .Where(c => c.Items.Count > 0 && c.Items.All(i => available.ContainsKey(i.ItemId)))
                .Select(c => new
                {
                    Combo = c,
                    PartsPerSet = ComboEngine.ItemsTotal(c.Items.Select(i => (unitPrice[i.ItemId], i.Quantity))),
                })
                .Where(x => x.PartsPerSet > x.Combo.Price)
                .OrderByDescending(x => x.PartsPerSet - x.Combo.Price).ThenBy(x => x.Combo.Id)
                .ToList();

            // Greedy allocation so a line is never counted in two combos.
            var desired = new Dictionary<int, (Combo Combo, int Sets, decimal Saving)>();
            var remaining = new Dictionary<int, int>(available);
            foreach (var c in candidates)
            {
                var sets = ComboEngine.CountSets(c.Combo.Items.Select(i => (i.ItemId, i.Quantity)), remaining);
                if (sets <= 0) continue;
                foreach (var i in c.Combo.Items) remaining[i.ItemId] -= sets * i.Quantity;
                desired[c.Combo.Id] = (c.Combo, sets, ComboEngine.Saving(sets, c.Combo.Price, c.PartsPerSet));
            }

            var result = new List<AutoApplyComboDto>();
            var comboIds = desired.Keys.Union(apps.Select(a => a.ComboId)).OrderBy(id => id).ToList();
            foreach (var comboId in comboIds)
            {
                var app = apps.FirstOrDefault(a => a.ComboId == comboId);
                var stored = app?.Amount ?? 0m;
                desired.TryGetValue(comboId, out var want);
                var combo = want.Combo ?? app?.Combo;
                if (combo is null) continue;

                int sets; decimal target;
                if (want.Combo != null) { sets = want.Sets; target = want.Saving; }
                else if (combo.IsActive) { sets = 0; target = 0m; }          // lines no longer form a set → refund
                else { sets = app!.Quantity; target = stored; }               // combo deactivated later → keep

                var delta = Math.Round(stored - target, 2);
                if (delta < 0 && trx.TotalPrice + delta < 0)
                {
                    delta = -trx.TotalPrice;
                    target = Math.Round(stored - delta, 2);
                }

                if (delta == 0m)
                {
                    if (stored > 0) result.Add(new AutoApplyComboDto(combo.Id, combo.Name, app?.Quantity ?? sets, stored, 0m));
                    continue;
                }

                var version = (app?.Version ?? 0) + 1;
                var adj = await _adjuster.ApplyAsync(trx.Id, delta, $"combo:{combo.Id}:tx{trx.Id}:v{version}",
                    $"Combo '{combo.Name}' re-evaluated: {sets} set(s), saving {stored:F2} -> {target:F2}", ct);
                if (!adj.Success)
                {
                    messages.Add($"Combo '{combo.Name}' could not be updated: {adj.Error}");
                    if (stored > 0) result.Add(new AutoApplyComboDto(combo.Id, combo.Name, app?.Quantity ?? 0, stored, 0m));
                    continue;
                }

                if (app is null)
                {
                    app = new ComboApplication { ComboId = combo.Id, TransactionId = trx.Id, BranchId = _user.BranchId };
                    _db.Add(app);
                }
                app.Quantity = sets;
                app.Amount = target;
                app.Version = version;
                app.On = DateTime.UtcNow;
                app.By = by;

                messages.Add(delta < 0
                    ? $"Combo '{combo.Name}' × {sets}: {-delta:F2} off (total saving {target:F2})."
                    : $"Combo '{combo.Name}': {delta:F2} restored (total saving {target:F2}).");
                result.Add(new AutoApplyComboDto(combo.Id, combo.Name, sets, target, delta));
            }
            return result;
        }
    }
}
