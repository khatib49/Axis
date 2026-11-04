using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Application.Services.OrderingChannels;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Phase4
{
    /// <summary>
    /// Connected delivery apps (Toters and similar). Orders still arrive through
    /// the existing webhook; this adds:
    ///  - item mapping (app item id → Axis item) used by the webhook,
    ///  - a menu feed the app pulls (prices with the app markup, availability),
    ///  - status updates pushed back to the app, signed and retried,
    ///  - optional auto-accept of new app orders.
    /// Shared secret: IntegrationSettings "Delivery.WebhookSecret.{source}".
    /// Optional bearer key for the push: "Delivery.ApiKey.{source}".
    /// </summary>
    public class DeliveryPlatformService : IDeliveryPlatformService
    {
        public const string ApiKeyPrefix = "Delivery.ApiKey.";
        public const string SignatureHeader = "X-Axis-Signature";
        public const string AxisItemPrefix = "axis-";
        private const int EnabledItemStatus = 1;

        public static readonly IReadOnlyDictionary<string, string> PlatformStatus = new Dictionary<string, string>
        {
            [DeliveryStatuses.Pending] = "received",
            [DeliveryStatuses.Preparing] = "accepted",
            [DeliveryStatuses.Ready] = "ready_for_pickup",
            [DeliveryStatuses.OutForDelivery] = "picked_up",
            [DeliveryStatuses.Delivered] = "delivered",
            [DeliveryStatuses.Cancelled] = "cancelled",
        };

        private readonly ApplicationDbContext _db;
        private readonly IIntegrationSettingsService _settings;
        private readonly IDeliveryService _delivery;
        private readonly IHttpClientFactory _http;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<DeliveryPlatformService> _log;

        public DeliveryPlatformService(ApplicationDbContext db, IIntegrationSettingsService settings, IDeliveryService delivery,
            IHttpClientFactory http, ICurrentUserAccessor user, ILogger<DeliveryPlatformService> log)
        { _db = db; _settings = settings; _delivery = delivery; _http = http; _user = user; _log = log; }

        // ── Platforms ──────────────────────────────────────────────────
        public async Task<List<DeliveryPlatformDto>> ListAsync(CancellationToken ct = default)
        {
            var list = await _db.Set<DeliveryPlatform>().AsNoTracking().OrderBy(p => p.DisplayName).ToListAsync(ct);
            var result = new List<DeliveryPlatformDto>();
            foreach (var p in list) result.Add(await ToDtoAsync(p, ct));
            return result;
        }

        private async Task<DeliveryPlatformDto> ToDtoAsync(DeliveryPlatform p, CancellationToken ct)
        {
            var mapped = await _db.Set<DeliveryItemMapping>().CountAsync(m => m.PlatformId == p.Id, ct);
            var pending = await _db.Set<DeliveryPlatformPush>().CountAsync(x => x.PlatformId == p.Id && x.State == DeliveryPushStates.Pending, ct);
            var failed = await _db.Set<DeliveryPlatformPush>().CountAsync(x => x.PlatformId == p.Id && x.State == DeliveryPushStates.Failed, ct);
            var secret = await _settings.GetRawAsync(DeliveryService.WebhookSecretKeyPrefix + p.Source, ct);
            return new(p.Id, p.Source, p.DisplayName, p.IsActive, p.StatusCallbackUrl, p.PriceMarkupPercent, p.AutoAccept, p.MappedItemsOnly,
                !string.IsNullOrWhiteSpace(secret), $"/api/delivery/webhook/{p.Source}", $"/api/delivery/platforms/{p.Source}/menu", mapped, pending, failed);
        }

        public async Task<BaseResponse<DeliveryPlatformDto>> CreateAsync(DeliveryPlatformUpsertDto dto, CancellationToken ct = default)
        {
            var err = Validate(dto);
            if (err is not null) return new(false, "Validation", err);
            var src = Slug(dto.Source);
            if (await _db.Set<DeliveryPlatform>().AnyAsync(p => p.Source == src, ct)) return new(false, "Duplicate", $"'{src}' is already connected.");
            var p = new DeliveryPlatform { Source = src, CreatedBy = _user.UserName, CreatedOn = DateTime.UtcNow };
            Apply(p, dto);
            _db.Add(p);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Delivery app connected.", await ToDtoAsync(p, ct));
        }

        public async Task<BaseResponse<DeliveryPlatformDto>> UpdateAsync(int id, DeliveryPlatformUpsertDto dto, CancellationToken ct = default)
        {
            var p = await _db.Set<DeliveryPlatform>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return new(false, "NotFound", "Delivery app not found.");
            var err = Validate(dto);
            if (err is not null) return new(false, "Validation", err);
            if (Slug(dto.Source) != p.Source) return new(false, "Validation", "The source name cannot change: it is part of the webhook URL the app already uses.");
            Apply(p, dto);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Delivery app updated.", await ToDtoAsync(p, ct));
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var p = await _db.Set<DeliveryPlatform>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return false;
            _db.Remove(p);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        private static void Apply(DeliveryPlatform p, DeliveryPlatformUpsertDto d)
        {
            p.DisplayName = d.DisplayName.Trim();
            p.IsActive = d.IsActive;
            p.StatusCallbackUrl = string.IsNullOrWhiteSpace(d.StatusCallbackUrl) ? null : d.StatusCallbackUrl.Trim();
            p.PriceMarkupPercent = Math.Round(d.PriceMarkupPercent, 2);
            p.AutoAccept = d.AutoAccept;
            p.MappedItemsOnly = d.MappedItemsOnly;
        }

        private static string Slug(string? s) => (s ?? "").Trim().ToLowerInvariant();

        private static string? Validate(DeliveryPlatformUpsertDto d)
        {
            var src = Slug(d.Source);
            if (src.Length is 0 or > 60 || !src.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                return "Source must be 1-60 letters, digits, '-' or '_' (for example 'toters').";
            if (src is "kiosk" or "selforder" or "shop") return $"'{src}' is reserved for Axis's own channels.";
            if (string.IsNullOrWhiteSpace(d.DisplayName) || d.DisplayName.Trim().Length > 120) return "Display name is required (max 120).";
            if (d.PriceMarkupPercent is < 0 or > 100) return "Markup must be between 0 and 100 percent.";
            if (!string.IsNullOrWhiteSpace(d.StatusCallbackUrl)
                && (!Uri.TryCreate(d.StatusCallbackUrl.Trim(), UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http")))
                return "Status callback URL must be an absolute http(s) URL.";
            return null;
        }

        // ── Item mapping ───────────────────────────────────────────────
        public async Task<List<DeliveryItemMappingDto>> ListMappingsAsync(int platformId, CancellationToken ct = default)
            => await (from m in _db.Set<DeliveryItemMapping>().AsNoTracking()
                      join i in _db.Set<Item>().AsNoTracking() on m.ItemId equals i.Id
                      where m.PlatformId == platformId
                      orderby i.Name
                      select new DeliveryItemMappingDto(m.Id, m.PlatformId, m.ExternalItemId, m.ItemId, i.Name, i.Price, m.PriceOverride, m.IsAvailable))
                .ToListAsync(ct);

        public async Task<BaseResponse<DeliveryItemMappingDto>> UpsertMappingAsync(int platformId, DeliveryItemMappingUpsertDto dto, CancellationToken ct = default)
        {
            if (!await _db.Set<DeliveryPlatform>().AnyAsync(p => p.Id == platformId, ct)) return new(false, "NotFound", "Delivery app not found.");
            var ext = (dto.ExternalItemId ?? "").Trim();
            if (ext.Length is 0 or > 120) return new(false, "Validation", "App item id is required (max 120).");
            if (!await _db.Set<Item>().AnyAsync(i => i.Id == dto.ItemId, ct)) return new(false, "Validation", "Item not found.");
            if (dto.PriceOverride is < 0) return new(false, "Validation", "Price cannot be negative.");

            var m = await _db.Set<DeliveryItemMapping>().FirstOrDefaultAsync(x => x.PlatformId == platformId && x.ExternalItemId == ext, ct);
            if (m is null)
            {
                // One app id per Axis item: re-mapping an item replaces its old id.
                var old = await _db.Set<DeliveryItemMapping>().Where(x => x.PlatformId == platformId && x.ItemId == dto.ItemId).ToListAsync(ct);
                _db.RemoveRange(old);
                m = new DeliveryItemMapping { PlatformId = platformId, ExternalItemId = ext };
                _db.Add(m);
            }
            m.ItemId = dto.ItemId;
            m.PriceOverride = dto.PriceOverride;
            m.IsAvailable = dto.IsAvailable;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Mapping saved.", (await ListMappingsAsync(platformId, ct)).First(x => x.Id == m.Id));
        }

        public async Task<bool> DeleteMappingAsync(int platformId, int mappingId, CancellationToken ct = default)
        {
            var m = await _db.Set<DeliveryItemMapping>().FirstOrDefaultAsync(x => x.Id == mappingId && x.PlatformId == platformId, ct);
            if (m is null) return false;
            _db.Remove(m);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        /// <summary>Webhook helper: app item id → Axis item id (mapping first, then "axis-{id}").</summary>
        public static async Task<int?> ResolveExternalItemAsync(ApplicationDbContext db, string source, string externalId, CancellationToken ct)
        {
            var ext = externalId.Trim();
            var mapped = await (from m in db.Set<DeliveryItemMapping>().AsNoTracking()
                                join p in db.Set<DeliveryPlatform>().AsNoTracking() on m.PlatformId equals p.Id
                                where p.Source == source && m.ExternalItemId == ext
                                select (int?)m.ItemId).FirstOrDefaultAsync(ct);
            if (mapped is not null) return mapped;
            if (ext.StartsWith(AxisItemPrefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(ext[AxisItemPrefix.Length..], out var id)
                && await db.Set<Item>().AnyAsync(i => i.Id == id, ct))
                return id;
            return null;
        }

        // ── Menu feed ──────────────────────────────────────────────────
        public async Task<BaseResponse<DeliveryMenuFeedDto>> MenuFeedAsync(string source, string? providedSecret, string assetBaseUrl, CancellationToken ct = default)
        {
            var src = Slug(source);
            var p = await _db.Set<DeliveryPlatform>().AsNoTracking().FirstOrDefaultAsync(x => x.Source == src, ct);
            if (p is null || !p.IsActive) return new(false, "NotFound", "Unknown delivery app.");
            var expected = await _settings.GetRawAsync(DeliveryService.WebhookSecretKeyPrefix + src, ct);
            if (string.IsNullOrWhiteSpace(expected)) return new(false, "NotConfigured", "No shared secret configured for this app.");
            if (!FixedEquals(expected, providedSecret)) return new(false, "Unauthorized", "Invalid secret.");
            return new(true, null, "Menu feed.", await BuildFeedAsync(p, assetBaseUrl, ct));
        }

        public async Task<BaseResponse<DeliveryMenuFeedDto>> MenuPreviewAsync(int platformId, string assetBaseUrl, CancellationToken ct = default)
        {
            var p = await _db.Set<DeliveryPlatform>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == platformId, ct);
            if (p is null) return new(false, "NotFound", "Delivery app not found.");
            return new(true, null, "Menu feed preview.", await BuildFeedAsync(p, assetBaseUrl, ct));
        }

        private async Task<DeliveryMenuFeedDto> BuildFeedAsync(DeliveryPlatform p, string assetBaseUrl, CancellationToken ct)
        {
            var maps = await _db.Set<DeliveryItemMapping>().AsNoTracking().Where(m => m.PlatformId == p.Id).ToDictionaryAsync(m => m.ItemId, ct);
            var items = await _db.Set<Item>().AsNoTracking()
                .Where(i => i.StatusId == EnabledItemStatus && i.SellOnline)
                .Select(i => new { i.Id, i.Name, i.Price, i.ImagePath, i.CategoryId, CategoryName = i.Category.Name })
                .ToListAsync(ct);
            if (p.MappedItemsOnly) items = items.Where(i => maps.ContainsKey(i.Id)).ToList();
            var baseUrl = (assetBaseUrl ?? "").TrimEnd('/');
            var factor = 1m + p.PriceMarkupPercent / 100m;
            var cats = items.GroupBy(i => new { i.CategoryId, i.CategoryName }).OrderBy(g => g.Key.CategoryName)
                .Select(g => new DeliveryMenuFeedCategoryDto(g.Key.CategoryId, g.Key.CategoryName, g.OrderBy(i => i.Name).Select(i =>
                {
                    maps.TryGetValue(i.Id, out var m);
                    var price = m?.PriceOverride ?? Math.Round(i.Price * factor, 2, MidpointRounding.AwayFromZero);
                    var img = string.IsNullOrWhiteSpace(i.ImagePath) ? null
                        : i.ImagePath.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? i.ImagePath : $"{baseUrl}/{i.ImagePath.TrimStart('/')}";
                    return new DeliveryMenuFeedItemDto(m?.ExternalItemId ?? $"{AxisItemPrefix}{i.Id}", i.Id, i.Name, price, m?.IsAvailable ?? true, img);
                }).ToList())).ToList();
            return new(p.Source, "USD", DateTime.UtcNow, cats);
        }

        // ── Auto-accept ────────────────────────────────────────────────
        public async Task OnOrderIngestedAsync(string source, int deliveryOrderId, CancellationToken ct = default)
        {
            var src = Slug(source);
            var p = await _db.Set<DeliveryPlatform>().AsNoTracking().FirstOrDefaultAsync(x => x.Source == src && x.IsActive, ct);
            if (p is null || !p.AutoAccept) return;
            var r = await _delivery.AdvanceAsync(deliveryOrderId, DeliveryStatuses.Preparing, $"Auto-accepted ({p.DisplayName})", ct);
            if (!r.Success) _log.LogWarning("Auto-accept of {Source} order {Order} failed: {Msg}", src, deliveryOrderId, r.Message);
        }

        // ── Status push ────────────────────────────────────────────────
        public async Task<DeliveryPushRunResultDto> RunPushesAsync(CancellationToken ct = default)
        {
            var enqueued = await EnqueueAsync(ct);
            var now = DateTime.UtcNow;
            var due = await _db.Set<DeliveryPlatformPush>()
                .Where(x => x.State == DeliveryPushStates.Pending && (x.NextAttemptOn == null || x.NextAttemptOn <= now))
                .OrderBy(x => x.Id).Take(100).ToListAsync(ct);
            int sent = 0, failed = 0, retrying = 0;
            var platforms = await _db.Set<DeliveryPlatform>().AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
            foreach (var push in due)
            {
                if (!platforms.TryGetValue(push.PlatformId, out var p) || string.IsNullOrWhiteSpace(p.StatusCallbackUrl) || !p.IsActive)
                {
                    push.State = DeliveryPushStates.Failed; push.LastError = "App is inactive or has no status callback URL."; failed++;
                    continue;
                }
                var note = await _db.Set<DeliveryStatusEvent>().AsNoTracking().Where(e => e.Id == push.StatusEventId).Select(e => e.Note).FirstOrDefaultAsync(ct);
                var body = JsonSerializer.Serialize(new
                {
                    @event = "order.status",
                    source = p.Source,
                    externalRef = push.ExternalRef,
                    axisOrderId = push.DeliveryOrderId,
                    status = PlatformStatus.TryGetValue(push.Status, out var ps) ? ps : push.Status.ToLowerInvariant(),
                    axisStatus = push.Status,
                    note,
                    at = push.CreatedOn,
                });
                var (ok, code, error) = await PostSignedAsync(p, body, ct);
                push.Attempts++;
                push.LastHttpStatus = code;
                if (ok)
                {
                    push.State = DeliveryPushStates.Sent; push.SentOn = DateTime.UtcNow; push.LastError = null; sent++;
                }
                else
                {
                    push.LastError = error?.Length > 1000 ? error[..1000] : error;
                    if (push.Attempts >= DeliveryPushStates.MaxAttempts) { push.State = DeliveryPushStates.Failed; failed++; }
                    else { push.NextAttemptOn = DateTime.UtcNow.AddMinutes(Math.Pow(2, push.Attempts - 1)); retrying++; }
                }
            }
            await _db.SaveChangesAsync(ct);
            return new(enqueued, sent, failed, retrying);
        }

        /// <summary>One push per status event of an app order, for apps with a callback URL. Events before the app was connected are skipped.</summary>
        private async Task<int> EnqueueAsync(CancellationToken ct)
        {
            var platforms = await _db.Set<DeliveryPlatform>().AsNoTracking()
                .Where(p => p.IsActive && p.StatusCallbackUrl != null && p.StatusCallbackUrl != "").ToListAsync(ct);
            var count = 0;
            foreach (var p in platforms)
            {
                var events = await (from e in _db.Set<DeliveryStatusEvent>().AsNoTracking()
                                    join o in _db.Set<DeliveryOrder>().AsNoTracking() on e.DeliveryOrderId equals o.Id
                                    where o.ExternalSource == p.Source && o.ExternalRef != null && e.On >= p.CreatedOn
                                          && !_db.Set<DeliveryPlatformPush>().Any(x => x.StatusEventId == e.Id)
                                    orderby e.Id
                                    select new { e.Id, e.DeliveryOrderId, e.ToStatus, e.FromStatus, e.On, o.ExternalRef })
                    .Take(500).ToListAsync(ct);
                // Driver assignment writes an event with the same status: nothing new to tell the app.
                foreach (var e in events.Where(e => e.FromStatus != e.ToStatus))
                {
                    _db.Add(new DeliveryPlatformPush
                    {
                        PlatformId = p.Id, DeliveryOrderId = e.DeliveryOrderId, StatusEventId = e.Id, ExternalRef = e.ExternalRef!,
                        Status = e.ToStatus, State = DeliveryPushStates.Pending, CreatedOn = e.On,
                    });
                    count++;
                }
                // Mark same-status events as handled so they are not re-scanned forever.
                foreach (var e in events.Where(e => e.FromStatus == e.ToStatus))
                {
                    _db.Add(new DeliveryPlatformPush
                    {
                        PlatformId = p.Id, DeliveryOrderId = e.DeliveryOrderId, StatusEventId = e.Id, ExternalRef = e.ExternalRef!,
                        Status = e.ToStatus, State = DeliveryPushStates.Sent, CreatedOn = e.On, SentOn = DateTime.UtcNow, LastError = "Skipped: no status change.",
                    });
                }
            }
            if (count > 0 || _db.ChangeTracker.HasChanges()) await _db.SaveChangesAsync(ct);
            return count;
        }

        private async Task<(bool ok, int? code, string? error)> PostSignedAsync(DeliveryPlatform p, string body, CancellationToken ct)
        {
            var secret = await _settings.GetRawAsync(DeliveryService.WebhookSecretKeyPrefix + p.Source, ct);
            if (string.IsNullOrWhiteSpace(secret)) return (false, null, "No shared secret configured: updates are not signed, so none are sent.");
            var apiKey = await _settings.GetRawAsync(ApiKeyPrefix + p.Source, ct);
            try
            {
                using var http = _http.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                using var req = new HttpRequestMessage(HttpMethod.Post, p.StatusCallbackUrl) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
                req.Headers.Add(SignatureHeader, "sha256=" + Sign(secret, body));
                req.Headers.Add("X-Axis-Source", p.Source);
                if (!string.IsNullOrWhiteSpace(apiKey)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                using var resp = await http.SendAsync(req, ct);
                var code = (int)resp.StatusCode;
                if (resp.IsSuccessStatusCode) return (true, code, null);
                var text = await resp.Content.ReadAsStringAsync(ct);
                return (false, code, $"HTTP {code}: {text}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                return (false, null, ex.Message);
            }
        }

        public static string Sign(string secret, string body)
            => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret.Trim()), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        private static bool FixedEquals(string expected, string? provided)
        {
            if (string.IsNullOrEmpty(provided)) return false;
            var a = Encoding.UTF8.GetBytes(expected.Trim());
            var b = Encoding.UTF8.GetBytes(provided.Trim());
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        public async Task<List<DeliveryPlatformPushDto>> ListPushesAsync(int platformId, int take, CancellationToken ct = default)
            => (await _db.Set<DeliveryPlatformPush>().AsNoTracking().Where(x => x.PlatformId == platformId && x.LastError != "Skipped: no status change.")
                    .OrderByDescending(x => x.Id).Take(Math.Clamp(take, 1, 200)).ToListAsync(ct))
                .Select(x => new DeliveryPlatformPushDto(x.Id, x.DeliveryOrderId, x.ExternalRef, x.Status,
                    PlatformStatus.TryGetValue(x.Status, out var ps) ? ps : x.Status, x.State, x.Attempts, x.LastHttpStatus, x.LastError, x.CreatedOn, x.SentOn, x.NextAttemptOn))
                .ToList();

        public async Task<bool> RetryPushAsync(int platformId, int pushId, CancellationToken ct = default)
        {
            var x = await _db.Set<DeliveryPlatformPush>().FirstOrDefaultAsync(p => p.Id == pushId && p.PlatformId == platformId, ct);
            if (x is null || x.State == DeliveryPushStates.Sent) return false;
            x.State = DeliveryPushStates.Pending; x.Attempts = 0; x.NextAttemptOn = null;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<DeliveryCallbackTestDto> TestCallbackAsync(int platformId, CancellationToken ct = default)
        {
            var p = await _db.Set<DeliveryPlatform>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == platformId, ct);
            if (p is null) return new(false, null, "Delivery app not found.");
            if (string.IsNullOrWhiteSpace(p.StatusCallbackUrl)) return new(false, null, "Set a status callback URL first.");
            var body = JsonSerializer.Serialize(new { @event = "ping", source = p.Source, at = DateTime.UtcNow });
            var (ok, code, error) = await PostSignedAsync(p, body, ct);
            return new(ok, code, ok ? $"The app answered HTTP {code}." : error ?? "Failed.");
        }
    }

    public class DeliveryPlatformJobs
    {
        private readonly IDeliveryPlatformService _svc;
        public DeliveryPlatformJobs(IDeliveryPlatformService svc) { _svc = svc; }
        public Task PushStatusesAsync(CancellationToken ct) => _svc.RunPushesAsync(ct);
    }
}
