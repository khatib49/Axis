using System.Text.Json;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Application.Services.Ai;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Phase4
{
    /// <summary>
    /// Upsell suggestions for the till, waiter, shop, self-order and kiosk.
    /// 1. Rules pinned by the manager come first.
    /// 2. Learned pairs: market-basket analysis over paid invoices of the last
    ///    90 days (confidence and lift), rebuilt nightly.
    /// 3. When Anthropic.ApiKey is set, Claude writes a short pitch per pair.
    /// Every suggestion shown / accepted is logged for the acceptance report.
    /// </summary>
    public class UpsellService : IUpsellService
    {
        public const int PaidStatus = 6;
        public const int EnabledItemStatus = 1;
        public const int MinPairCount = 3;
        public const int MaxPerItem = 8;
        public const int MaxItemsPerInvoice = 30;
        public const int MaxPitchesPerRebuild = 40;
        public const string AiPitchSettingKey = "Upsell.AiPitches";
        private const string FallbackModel = "claude-sonnet-4-6";

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly IIntegrationSettingsService _settings;
        private readonly ClaudeApiClient _claude;
        private readonly ILogger<UpsellService> _log;

        public UpsellService(ApplicationDbContext db, ICurrentUserAccessor user, IIntegrationSettingsService settings, ClaudeApiClient claude, ILogger<UpsellService> log)
        { _db = db; _user = user; _settings = settings; _claude = claude; _log = log; }

        // ── Suggest ────────────────────────────────────────────────────
        public async Task<List<UpsellSuggestionDto>> SuggestAsync(UpsellSuggestRequest req, bool publicOnly, CancellationToken ct = default)
        {
            var cart = (req.ItemIds ?? new()).Where(i => i > 0).Distinct().Take(50).ToList();
            if (cart.Count == 0) return new();
            var limit = Math.Clamp(req.Limit ?? 3, 1, 6);

            var cartItems = await _db.Set<Item>().AsNoTracking().Where(i => cart.Contains(i.Id))
                .Select(i => new { i.Id, i.Name, i.CategoryId }).ToListAsync(ct);
            var cartCats = cartItems.Select(i => i.CategoryId).Distinct().ToList();
            var names = cartItems.ToDictionary(i => i.Id, i => i.Name);

            var rules = await _db.Set<UpsellRule>().AsNoTracking()
                .Where(r => r.IsActive && !cart.Contains(r.SuggestItemId)
                    && ((r.TriggerItemId != null && cart.Contains(r.TriggerItemId.Value))
                        || (r.TriggerCategoryId != null && cartCats.Contains(r.TriggerCategoryId.Value))))
                .OrderByDescending(r => r.Priority).ThenBy(r => r.Id)
                .ToListAsync(ct);

            var pairs = await _db.Set<UpsellPair>().AsNoTracking()
                .Where(p => cart.Contains(p.ItemId) && !cart.Contains(p.SuggestItemId))
                .ToListAsync(ct);
            var learned = pairs.GroupBy(p => p.SuggestItemId)
                .Select(g =>
                {
                    var best = g.OrderByDescending(p => p.Confidence).First();
                    return new { SuggestItemId = g.Key, Score = g.Sum(p => p.Confidence * Math.Min(p.Lift, 5m)), Best = best };
                })
                .OrderByDescending(x => x.Score).ToList();

            var candidateIds = rules.Select(r => r.SuggestItemId).Concat(learned.Select(l => l.SuggestItemId)).Distinct().ToList();
            if (candidateIds.Count == 0) return new();

            var q = _db.Set<Item>().AsNoTracking().Where(i => candidateIds.Contains(i.Id) && i.StatusId == EnabledItemStatus);
            if (publicOnly) q = q.Where(i => i.SellOnline);
            var items = await q.Select(i => new { i.Id, i.Name, i.Price, i.ImagePath, Category = i.Category.Name })
                .ToDictionaryAsync(i => i.Id, ct);

            var result = new List<UpsellSuggestionDto>();
            foreach (var r in rules)
            {
                if (result.Count >= limit) break;
                if (!items.TryGetValue(r.SuggestItemId, out var it) || result.Any(x => x.ItemId == it.Id)) continue;
                var trigger = r.TriggerItemId is int t && names.TryGetValue(t, out var tn) ? tn : null;
                var reason = trigger is null ? "Recommended with your order" : $"Recommended with {trigger}";
                result.Add(new(it.Id, it.Name, it.Price, it.ImagePath, it.Category, reason, r.Message, "Rule"));
            }
            foreach (var l in learned)
            {
                if (result.Count >= limit) break;
                if (!items.TryGetValue(l.SuggestItemId, out var it) || result.Any(x => x.ItemId == it.Id)) continue;
                var pct = (int)Math.Round(l.Best.Confidence * 100m);
                var trigger = names.TryGetValue(l.Best.ItemId, out var tn) ? tn : "this item";
                result.Add(new(it.Id, it.Name, it.Price, it.ImagePath, it.Category,
                    $"{pct}% of guests who order {trigger} add this", l.Best.Pitch, "Learned"));
            }
            return result;
        }

        public async Task<BaseResponse<bool>> RecordEventAsync(UpsellEventDto dto, bool publicOnly, CancellationToken ct = default)
        {
            var channel = UpsellChannels.All.FirstOrDefault(c => string.Equals(c, dto.Channel, StringComparison.OrdinalIgnoreCase));
            if (channel is null) return new(false, "Validation", $"Unknown channel. Use one of: {string.Join(", ", UpsellChannels.All)}.");
            if (publicOnly && !UpsellChannels.Public.Contains(channel)) return new(false, "Validation", "Guests can only log Shop, SelfOrder or Kiosk events.");
            var action = string.Equals(dto.Action, UpsellActions.Accepted, StringComparison.OrdinalIgnoreCase) ? UpsellActions.Accepted
                : string.Equals(dto.Action, UpsellActions.Shown, StringComparison.OrdinalIgnoreCase) ? UpsellActions.Shown : null;
            if (action is null) return new(false, "Validation", "Action must be Shown or Accepted.");
            var price = await _db.Set<Item>().AsNoTracking().Where(i => i.Id == dto.SuggestItemId).Select(i => (decimal?)i.Price).FirstOrDefaultAsync(ct);
            if (price is null) return new(false, "NotFound", "Item not found.");
            _db.Add(new UpsellEvent
            {
                Channel = channel, Action = action, SuggestItemId = dto.SuggestItemId,
                Source = string.Equals(dto.Source, "Rule", StringComparison.OrdinalIgnoreCase) ? "Rule" : "Learned",
                Price = price.Value, CreatedOn = DateTime.UtcNow, BranchId = _user.BranchId,
            });
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Logged.", true);
        }

        // ── Learn ──────────────────────────────────────────────────────
        public async Task<UpsellRebuildResultDto> RebuildAsync(int days = 90, bool writePitches = true, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 7, 365);
            var since = DateTime.UtcNow.AddDays(-days);
            var rows = await (from ti in _db.Set<TransactionItem>().AsNoTracking()
                              join t in _db.Set<TransactionRecord>().AsNoTracking() on ti.TransactionRecordId equals t.Id
                              where t.StatusId == PaidStatus && t.CreatedOn >= since && ti.Quantity > 0
                              select new { ti.TransactionRecordId, ti.ItemId })
                .Distinct().ToListAsync(ct);

            var baskets = rows.GroupBy(r => r.TransactionRecordId).Select(g => g.Select(x => x.ItemId).Distinct().ToArray()).ToList();
            var computed = ComputePairs(baskets, MinPairCount, MaxPerItem);
            var now = DateTime.UtcNow;

            var old = await _db.Set<UpsellPair>().ToListAsync(ct);
            var oldPitch = old.Where(p => p.Pitch != null).ToDictionary(p => (p.ItemId, p.SuggestItemId), p => p.Pitch);
            // Only keep pairs whose items still exist (deleted items would break the FK).
            var ids = computed.SelectMany(p => new[] { p.ItemId, p.SuggestItemId }).Distinct().ToList();
            var existing = (await _db.Set<Item>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct)).ToHashSet();

            await using (var tx = await _db.Database.BeginTransactionAsync(ct))
            {
                _db.RemoveRange(old);
                await _db.SaveChangesAsync(ct);
                foreach (var p in computed.Where(p => existing.Contains(p.ItemId) && existing.Contains(p.SuggestItemId)))
                {
                    _db.Add(new UpsellPair
                    {
                        ItemId = p.ItemId, SuggestItemId = p.SuggestItemId, PairCount = p.PairCount,
                        Confidence = p.Confidence, Lift = p.Lift, ComputedOn = now,
                        Pitch = oldPitch.TryGetValue((p.ItemId, p.SuggestItemId), out var pitch) ? pitch : null,
                    });
                }
                await _db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }

            var kept = await _db.Set<UpsellPair>().CountAsync(ct);
            int written = 0; string? note = null;
            if (writePitches) (written, note) = await WritePitchesAsync(ct);
            _log.LogInformation("Upsell rebuild: {Invoices} invoices, {Pairs} pairs, {Pitches} AI pitches", baskets.Count, kept, written);
            return new(baskets.Count, kept, written, now, note);
        }

        public record ComputedPair(int ItemId, int SuggestItemId, int PairCount, decimal Confidence, decimal Lift);

        /// <summary>Market-basket pairs: confidence = P(B|A), lift = confidence / P(B). Keeps lift &gt; 1 and the top N per item.</summary>
        public static List<ComputedPair> ComputePairs(IReadOnlyList<int[]> baskets, int minPairCount, int maxPerItem)
        {
            var n = baskets.Count;
            if (n == 0) return new();
            var itemCount = new Dictionary<int, int>();
            var pairCount = new Dictionary<(int, int), int>();
            foreach (var raw in baskets)
            {
                var b = raw.Distinct().ToArray();
                foreach (var i in b) itemCount[i] = itemCount.TryGetValue(i, out var c) ? c + 1 : 1;
                if (b.Length < 2 || b.Length > MaxItemsPerInvoice) continue;
                for (var x = 0; x < b.Length; x++)
                    for (var y = 0; y < b.Length; y++)
                    {
                        if (x == y) continue;
                        var k = (b[x], b[y]);
                        pairCount[k] = pairCount.TryGetValue(k, out var c) ? c + 1 : 1;
                    }
            }
            return pairCount
                .Where(kv => kv.Value >= minPairCount)
                .Select(kv =>
                {
                    var (a, s) = kv.Key;
                    var conf = (decimal)kv.Value / itemCount[a];
                    var baseRate = (decimal)itemCount[s] / n;
                    var lift = baseRate == 0 ? 0 : conf / baseRate;
                    return new ComputedPair(a, s, kv.Value, Math.Round(conf, 4), Math.Round(Math.Min(lift, 99999m), 4));
                })
                .Where(p => p.Lift > 1m)
                .GroupBy(p => p.ItemId)
                .SelectMany(g => g.OrderByDescending(p => p.Confidence * Math.Min(p.Lift, 5m)).ThenByDescending(p => p.PairCount).Take(maxPerItem))
                .ToList();
        }

        private async Task<(int, string?)> WritePitchesAsync(CancellationToken ct)
        {
            var flag = await _settings.GetRawAsync(AiPitchSettingKey, ct);
            if (string.Equals(flag?.Trim(), "false", StringComparison.OrdinalIgnoreCase)) return (0, "AI pitches are turned off (Upsell.AiPitches = false).");
            var apiKey = await _settings.GetRawAsync("Anthropic.ApiKey", ct);
            if (string.IsNullOrWhiteSpace(apiKey)) return (0, "No Anthropic.ApiKey set: suggestions use the data-based reason only.");
            var model = await _settings.GetRawAsync("Anthropic.Model", ct) ?? FallbackModel;

            var todo = await (from p in _db.Set<UpsellPair>()
                              join a in _db.Set<Item>() on p.ItemId equals a.Id
                              join s in _db.Set<Item>() on p.SuggestItemId equals s.Id
                              where p.Pitch == null
                              orderby p.PairCount descending
                              select new { Pair = p, A = a.Name, S = s.Name, s.Price })
                .Take(MaxPitchesPerRebuild).ToListAsync(ct);
            if (todo.Count == 0) return (0, null);

            var lines = string.Join("\n", todo.Select((t, i) => $"{i}|{t.A}|{t.S}|{t.Price:0.##}"));
            var system = "You write upsell lines for a cafe and restaurant POS. For each line 'index|item in cart|suggested item|price USD' write one friendly, honest pitch of at most 70 characters that offers the suggested item, in English, no emojis, no invented facts. Answer with JSON only: [{\"i\":0,\"pitch\":\"...\"}].";
            try
            {
                var resp = await _claude.CreateMessageAsync(apiKey, model, system,
                    new[] { new ClaudeMessage { Role = "user", Content = JsonSerializer.SerializeToElement(lines) } }, null, 2048, ct);
                var text = string.Concat(resp.Content?.Where(c => c.Type == "text").Select(c => c.Text ?? "") ?? Array.Empty<string>());
                var start = text.IndexOf('['); var end = text.LastIndexOf(']');
                if (start < 0 || end <= start) return (0, "AI answer was not JSON; pitches skipped.");
                var parsed = JsonSerializer.Deserialize<List<PitchLine>>(text[start..(end + 1)], new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                var written = 0;
                foreach (var p in parsed)
                {
                    if (p.I < 0 || p.I >= todo.Count || string.IsNullOrWhiteSpace(p.Pitch)) continue;
                    var pitch = p.Pitch.Trim();
                    todo[p.I].Pair.Pitch = pitch.Length > 200 ? pitch[..200] : pitch;
                    written++;
                }
                await _db.SaveChangesAsync(ct);
                return (written, null);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Upsell AI pitches failed; suggestions keep the data-based reason");
                return (0, "AI pitches failed: " + ex.Message);
            }
        }

        private sealed class PitchLine { public int I { get; set; } public string? Pitch { get; set; } }

        public async Task<List<UpsellPairDto>> ListPairsAsync(int? itemId, int take, CancellationToken ct = default)
        {
            var q = from p in _db.Set<UpsellPair>().AsNoTracking()
                    join a in _db.Set<Item>().AsNoTracking() on p.ItemId equals a.Id
                    join s in _db.Set<Item>().AsNoTracking() on p.SuggestItemId equals s.Id
                    select new { p, A = a.Name, S = s.Name };
            if (itemId is int id) q = q.Where(x => x.p.ItemId == id);
            return await q.OrderByDescending(x => x.p.PairCount).ThenByDescending(x => x.p.Confidence).Take(Math.Clamp(take, 1, 500))
                .Select(x => new UpsellPairDto(x.p.ItemId, x.A, x.p.SuggestItemId, x.S, x.p.PairCount, x.p.Confidence, x.p.Lift, x.p.Pitch, x.p.ComputedOn))
                .ToListAsync(ct);
        }

        // ── Rules ──────────────────────────────────────────────────────
        public async Task<List<UpsellRuleDto>> ListRulesAsync(CancellationToken ct = default)
            => await (from r in _db.Set<UpsellRule>().AsNoTracking()
                      join s in _db.Set<Item>().AsNoTracking() on r.SuggestItemId equals s.Id
                      join ti in _db.Set<Item>().AsNoTracking() on r.TriggerItemId equals (int?)ti.Id into tis
                      from ti in tis.DefaultIfEmpty()
                      join tc in _db.Set<Category>().AsNoTracking() on r.TriggerCategoryId equals (int?)tc.Id into tcs
                      from tc in tcs.DefaultIfEmpty()
                      orderby r.Priority descending, r.Id
                      select new UpsellRuleDto(r.Id, r.TriggerItemId, ti != null ? ti.Name : null, r.TriggerCategoryId, tc != null ? tc.Name : null,
                          r.SuggestItemId, s.Name, r.Message, r.Priority, r.IsActive))
                .ToListAsync(ct);

        public async Task<BaseResponse<UpsellRuleDto>> CreateRuleAsync(UpsellRuleUpsertDto dto, CancellationToken ct = default)
        {
            var err = await ValidateRuleAsync(dto, ct);
            if (err is not null) return new(false, "Validation", err);
            var r = new UpsellRule { CreatedBy = _user.UserName, CreatedOn = DateTime.UtcNow };
            Apply(r, dto);
            _db.Add(r);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Rule created.", (await ListRulesAsync(ct)).First(x => x.Id == r.Id));
        }

        public async Task<BaseResponse<UpsellRuleDto>> UpdateRuleAsync(int id, UpsellRuleUpsertDto dto, CancellationToken ct = default)
        {
            var r = await _db.Set<UpsellRule>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return new(false, "NotFound", "Rule not found.");
            var err = await ValidateRuleAsync(dto, ct);
            if (err is not null) return new(false, "Validation", err);
            Apply(r, dto);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Rule updated.", (await ListRulesAsync(ct)).First(x => x.Id == r.Id));
        }

        public async Task<bool> DeleteRuleAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<UpsellRule>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return false;
            _db.Remove(r);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        private static void Apply(UpsellRule r, UpsellRuleUpsertDto d)
        {
            r.TriggerItemId = d.TriggerItemId is > 0 ? d.TriggerItemId : null;
            r.TriggerCategoryId = r.TriggerItemId is null && d.TriggerCategoryId is > 0 ? d.TriggerCategoryId : null;
            r.SuggestItemId = d.SuggestItemId;
            r.Message = string.IsNullOrWhiteSpace(d.Message) ? null : d.Message.Trim();
            r.Priority = d.Priority;
            r.IsActive = d.IsActive;
        }

        private async Task<string?> ValidateRuleAsync(UpsellRuleUpsertDto d, CancellationToken ct)
        {
            if (d.TriggerItemId is not > 0 && d.TriggerCategoryId is not > 0) return "Pick a trigger item or a trigger category.";
            if (d.TriggerItemId is > 0 && d.TriggerItemId == d.SuggestItemId) return "An item cannot suggest itself.";
            if (!await _db.Set<Item>().AnyAsync(i => i.Id == d.SuggestItemId, ct)) return "Suggested item not found.";
            if (d.TriggerItemId is int ti && ti > 0 && !await _db.Set<Item>().AnyAsync(i => i.Id == ti, ct)) return "Trigger item not found.";
            if (d.TriggerItemId is not > 0 && d.TriggerCategoryId is int tc && !await _db.Set<Category>().AnyAsync(c => c.Id == tc, ct)) return "Trigger category not found.";
            if (d.Message is { Length: > 200 }) return "Message is limited to 200 characters.";
            return null;
        }

        // ── Report ─────────────────────────────────────────────────────
        public async Task<UpsellStatsDto> StatsAsync(int days, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 1, 365);
            var since = DateTime.UtcNow.AddDays(-days);
            var ev = await _db.Set<UpsellEvent>().AsNoTracking().Where(e => e.CreatedOn >= since)
                .Select(e => new { e.Channel, e.Action, e.SuggestItemId, e.Price }).ToListAsync(ct);
            static decimal Rate(int a, int s) => s == 0 ? 0 : Math.Round((decimal)a / s, 4);

            var byChannel = ev.GroupBy(e => e.Channel).Select(g =>
            {
                var s = g.Count(e => e.Action == UpsellActions.Shown); var a = g.Count(e => e.Action == UpsellActions.Accepted);
                return new UpsellChannelStatDto(g.Key, s, a, Rate(a, s), g.Where(e => e.Action == UpsellActions.Accepted).Sum(e => e.Price));
            }).OrderByDescending(c => c.Shown).ToList();

            var ids = ev.Select(e => e.SuggestItemId).Distinct().ToList();
            var names = await _db.Set<Item>().AsNoTracking().Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name, ct);
            var byItem = ev.GroupBy(e => e.SuggestItemId).Select(g =>
            {
                var s = g.Count(e => e.Action == UpsellActions.Shown); var a = g.Count(e => e.Action == UpsellActions.Accepted);
                return new UpsellItemStatDto(g.Key, names.TryGetValue(g.Key, out var n) ? n : $"#{g.Key}", s, a, Rate(a, s),
                    g.Where(e => e.Action == UpsellActions.Accepted).Sum(e => e.Price));
            }).OrderByDescending(i => i.Accepted).ThenByDescending(i => i.Shown).Take(50).ToList();

            var shown = ev.Count(e => e.Action == UpsellActions.Shown);
            var accepted = ev.Count(e => e.Action == UpsellActions.Accepted);
            return new(days, shown, accepted, Rate(accepted, shown), ev.Where(e => e.Action == UpsellActions.Accepted).Sum(e => e.Price), byChannel, byItem);
        }
    }

    public class UpsellJobs
    {
        private readonly IUpsellService _svc;
        public UpsellJobs(IUpsellService svc) { _svc = svc; }
        public Task RebuildAsync(CancellationToken ct) => _svc.RebuildAsync(90, true, ct);
    }
}
