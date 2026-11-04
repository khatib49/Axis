using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Phase5
{
    public interface IAlertService
    {
        Task<List<AlertRuleDto>> ListAsync(CancellationToken ct = default);
        Task<BaseResponse<AlertRuleDto>> CreateAsync(AlertRuleUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<AlertRuleDto>> UpdateAsync(int id, AlertRuleUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<AlertCheckDto>> TestAsync(int id, bool send, CancellationToken ct = default);
        Task<AlertRunResultDto> RunAsync(CancellationToken ct = default);
        Task<List<AlertEventDto>> EventsAsync(int take, CancellationToken ct = default);
        Task<decimal> MeasureAsync(string metric, int windowMinutes, CancellationToken ct = default);
    }

    /// <summary>
    /// Owner alerts: each rule watches one metric (sales, voids, stock, open tabs,
    /// delivery, cash variance, busy stations...) and sends a message to the
    /// owner's phone (Telegram / WhatsApp), email or the dashboard bell when the
    /// condition is met, at most once per cooldown and only inside its hours.
    /// </summary>
    public class AlertService : IAlertService
    {
        private const int PaidStatus = 6, OpenStatus = 7;
        private static readonly string[] Channels = { "Telegram", "WhatsApp", "Email", "InApp" };
        private readonly ApplicationDbContext _db;
        private readonly IIntegrationSettingsService _settings;
        private readonly IOwnerNotifier _notify;
        private readonly ILogger<AlertService> _log;

        public AlertService(ApplicationDbContext db, IIntegrationSettingsService settings, IOwnerNotifier notify, ILogger<AlertService> log)
        { _db = db; _settings = settings; _notify = notify; _log = log; }

        public static string Label(string metric) => metric switch
        {
            AlertMetrics.SalesToday => "Sales today ($)",
            AlertMetrics.NoSaleMinutes => "Minutes since the last sale",
            AlertMetrics.LargeSale => "Largest sale in the window ($)",
            AlertMetrics.VoidRequestsPending => "Void requests waiting",
            AlertMetrics.VoidAmountToday => "Approved voids today ($)",
            AlertMetrics.LowStockCount => "Items at or below reorder level",
            AlertMetrics.OpenInvoicesOlderThan => "Open invoices older than the window",
            AlertMetrics.DeliveryPendingOlderThan => "Delivery orders waiting longer than the window",
            AlertMetrics.CashVariance => "Largest cash variance at shift close ($)",
            AlertMetrics.StationsBusyPercent => "Stations playing (%)",
            _ => metric,
        };

        private static AlertRuleDto ToDto(AlertRule r) => new(r.Id, r.Name, r.Metric, r.Comparator, r.Threshold, r.WindowMinutes, r.FromHour, r.ToHour, r.Channels, r.Recipients, r.CooldownMinutes, r.IsActive, r.LastFiredOn);

        public async Task<List<AlertRuleDto>> ListAsync(CancellationToken ct = default)
            => (await _db.Set<AlertRule>().AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct)).Select(ToDto).ToList();

        private static string? Validate(AlertRuleUpsertDto d)
        {
            if (string.IsNullOrWhiteSpace(d.Name) || d.Name.Trim().Length > 120) return "Name is required (max 120).";
            if (!AlertMetrics.All.Contains(d.Metric)) return $"Metric must be one of {string.Join(", ", AlertMetrics.All)}.";
            if (d.Comparator is not ("Above" or "Below")) return "Comparator must be Above or Below.";
            if (d.WindowMinutes is < 1 or > 1440) return "Window must be 1 to 1440 minutes.";
            if (d.FromHour is < 0 or > 23 || d.ToHour is < 0 or > 24) return "Hours must be 0 to 23.";
            if (d.CooldownMinutes is < 5 or > 10080) return "Cooldown must be 5 minutes to 7 days.";
            var ch = OwnerNotifier.SplitList(d.Channels);
            if (ch.Count == 0 || ch.Any(c => !Channels.Contains(c, StringComparer.OrdinalIgnoreCase))) return "Channels: Telegram, WhatsApp, Email, InApp.";
            return null;
        }

        private static void Apply(AlertRule r, AlertRuleUpsertDto d)
        {
            r.Name = d.Name.Trim(); r.Metric = d.Metric; r.Comparator = d.Comparator; r.Threshold = d.Threshold; r.WindowMinutes = d.WindowMinutes;
            r.FromHour = d.FromHour; r.ToHour = d.ToHour; r.Channels = string.Join(",", OwnerNotifier.SplitList(d.Channels).Select(c => Channels.First(x => x.Equals(c, StringComparison.OrdinalIgnoreCase))));
            r.Recipients = string.IsNullOrWhiteSpace(d.Recipients) ? null : string.Join(",", OwnerNotifier.SplitList(d.Recipients));
            r.CooldownMinutes = d.CooldownMinutes; r.IsActive = d.IsActive;
        }

        public async Task<BaseResponse<AlertRuleDto>> CreateAsync(AlertRuleUpsertDto dto, CancellationToken ct = default)
        {
            var err = Validate(dto); if (err is not null) return new(false, "Validation", err);
            var r = new AlertRule(); Apply(r, dto); _db.Add(r); await _db.SaveChangesAsync(ct);
            return new(true, null, "Alert created.", ToDto(r));
        }

        public async Task<BaseResponse<AlertRuleDto>> UpdateAsync(int id, AlertRuleUpsertDto dto, CancellationToken ct = default)
        {
            var r = await _db.Set<AlertRule>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return new(false, "NotFound", "Alert not found.");
            var err = Validate(dto); if (err is not null) return new(false, "Validation", err);
            Apply(r, dto); await _db.SaveChangesAsync(ct);
            return new(true, null, "Alert saved.", ToDto(r));
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var r = await _db.Set<AlertRule>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return false;
            _db.Remove(r); await _db.SaveChangesAsync(ct); return true;
        }

        public async Task<decimal> MeasureAsync(string metric, int windowMinutes, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var since = now.AddMinutes(-windowMinutes);
            var today = VenueClock.TodayStartUtc(VenueClock.Zone(await _settings.GetRawAsync("Venue.TimeZone", ct)));
            var tx = _db.Set<TransactionRecord>().AsNoTracking();
            switch (metric)
            {
                case AlertMetrics.SalesToday:
                    return await tx.Where(t => t.StatusId == PaidStatus && (t.PaidOn ?? t.CreatedOn) >= today).SumAsync(t => (decimal?)t.TotalPrice, ct) ?? 0;
                case AlertMetrics.NoSaleMinutes:
                    var last = await tx.Where(t => t.StatusId == PaidStatus).MaxAsync(t => (DateTime?)(t.PaidOn ?? t.CreatedOn), ct);
                    return last is null ? 0 : (decimal)Math.Round((now - last.Value).TotalMinutes, 0);
                case AlertMetrics.LargeSale:
                    return await tx.Where(t => t.StatusId == PaidStatus && (t.PaidOn ?? t.CreatedOn) >= since).MaxAsync(t => (decimal?)t.TotalPrice, ct) ?? 0;
                case AlertMetrics.VoidRequestsPending:
                    return await _db.Set<VoidRequest>().AsNoTracking().CountAsync(v => v.Status == "Pending", ct);
                case AlertMetrics.VoidAmountToday:
                    return await (from v in _db.Set<VoidRequest>().AsNoTracking()
                                  join t in tx on v.TransactionId equals t.Id
                                  where v.Status == "Approved" && v.DecidedOn >= today
                                  select (decimal?)(v.Amount ?? t.TotalPrice)).SumAsync(ct) ?? 0;
                case AlertMetrics.LowStockCount:
                    return await _db.Set<Ingredient>().AsNoTracking().CountAsync(i => i.IsActive && i.ReorderLevel != null && i.QuantityOnHand <= i.ReorderLevel, ct);
                case AlertMetrics.OpenInvoicesOlderThan:
                    return await tx.CountAsync(t => t.StatusId == OpenStatus && t.CreatedOn < since, ct);
                case AlertMetrics.DeliveryPendingOlderThan:
                    return await _db.Set<DeliveryOrder>().AsNoTracking().CountAsync(o => o.Status == DeliveryStatuses.Pending && o.CreatedOn < since, ct);
                case AlertMetrics.CashVariance:
                    var v2 = await _db.Set<CashierShift>().AsNoTracking().Where(s => s.ClosedOn >= since && s.Variance != null).Select(s => s.Variance!.Value).ToListAsync(ct);
                    return v2.Count == 0 ? 0 : v2.Max(Math.Abs);
                case AlertMetrics.StationsBusyPercent:
                    var total = await _db.Set<Set>().AsNoTracking().CountAsync(s => s.StatusId == null || (s.StatusId != 2 && s.StatusId != 3), ct);
                    return total == 0 ? 0 : Math.Round((await StationBusy.RunningSetIdsAsync(_db, ct)).Count * 100m / total, 0);
                default: return 0;
            }
        }

        public static bool Triggered(string comparator, decimal value, decimal threshold) => comparator == "Below" ? value < threshold : value > threshold;

        public static bool InHours(int from, int to, int hour) => from == to || (from < to ? hour >= from && hour < to : hour >= from || hour < to);

        private static string Message(AlertRule r, decimal v) => $"{Label(r.Metric)} is {v:0.##} ({(r.Comparator == "Below" ? "below" : "above")} {r.Threshold:0.##}).";

        public async Task<BaseResponse<AlertCheckDto>> TestAsync(int id, bool send, CancellationToken ct = default)
        {
            var r = await _db.Set<AlertRule>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return new(false, "NotFound", "Alert not found.");
            var v = await MeasureAsync(r.Metric, r.WindowMinutes, ct);
            var hit = Triggered(r.Comparator, v, r.Threshold);
            var msg = Message(r, v);
            if (send)
            {
                var res = await _notify.NotifyOwnersAsync($"Test alert: {r.Name}", msg, OwnerNotifier.SplitList(r.Channels), r.Recipients, ct);
                msg += $" Sent {res.Sent}, failed {res.Failed}.{(res.Notes.Count > 0 ? " " + string.Join("; ", res.Notes) : "")}";
            }
            return new(true, null, msg, new(r.Id, v, hit, msg));
        }

        public async Task<AlertRunResultDto> RunAsync(CancellationToken ct = default)
        {
            var tz = VenueClock.Zone(await _settings.GetRawAsync("Venue.TimeZone", ct));
            var hour = VenueClock.LocalHour(tz);
            var rules = await _db.Set<AlertRule>().Where(r => r.IsActive).ToListAsync(ct);
            var results = new List<AlertCheckDto>(); var fired = 0;
            foreach (var r in rules)
            {
                if (!InHours(r.FromHour, r.ToHour, hour)) continue;
                if (r.LastFiredOn is DateTime lf && DateTime.UtcNow - lf < TimeSpan.FromMinutes(r.CooldownMinutes)) continue;
                decimal v;
                try { v = await MeasureAsync(r.Metric, r.WindowMinutes, ct); }
                catch (Exception ex) { _log.LogWarning(ex, "Alert {Rule} measure failed", r.Id); continue; }
                var hit = Triggered(r.Comparator, v, r.Threshold);
                var msg = Message(r, v);
                results.Add(new(r.Id, v, hit, msg));
                if (!hit) continue;
                var res = await _notify.NotifyOwnersAsync($"Axis alert: {r.Name}", msg, OwnerNotifier.SplitList(r.Channels), r.Recipients, ct);
                r.LastFiredOn = DateTime.UtcNow;
                _db.Add(new AlertEvent { RuleId = r.Id, Value = v, Message = msg, Delivery = $"sent {res.Sent}, failed {res.Failed}{(res.Notes.Count > 0 ? "; " + string.Join("; ", res.Notes) : "")}"[..Math.Min(300, $"sent {res.Sent}, failed {res.Failed}{(res.Notes.Count > 0 ? "; " + string.Join("; ", res.Notes) : "")}".Length)] });
                fired++;
            }
            await _db.SaveChangesAsync(ct);
            return new(results.Count, fired, results);
        }

        public async Task<List<AlertEventDto>> EventsAsync(int take, CancellationToken ct = default)
            => await (from e in _db.Set<AlertEvent>().AsNoTracking()
                      join r in _db.Set<AlertRule>().AsNoTracking() on e.RuleId equals r.Id
                      orderby e.Id descending
                      select new AlertEventDto(e.Id, e.RuleId, r.Name, e.Value, e.Message, e.Delivery, e.FiredOn)).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct);
    }

    public class Phase5Jobs
    {
        private readonly IAlertService _alerts;
        private readonly IParentWalletService _parents;
        public Phase5Jobs(IAlertService alerts, IParentWalletService parents) { _alerts = alerts; _parents = parents; }
        public async Task EveryFiveMinutesAsync(CancellationToken ct) { await _alerts.RunAsync(ct); await _parents.NotifySessionEndsAsync(ct); }
    }
}
