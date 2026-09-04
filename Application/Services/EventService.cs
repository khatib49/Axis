using System.Text.Json;
using Application.DTOs;
using Application.IServices;
using Application.Services.Payments;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class EventService : IEventService
    {
        private readonly IBaseRepository<Event> _repo;
        private readonly IBaseRepository<EventRegistration> _regRepo;
        private readonly IMediaStorageService _media;
        private readonly StripeGateway _stripe;
        private readonly WhishGateway _whish;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<EventService> _logger;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        public EventService(
            IBaseRepository<Event> repo,
            IBaseRepository<EventRegistration> regRepo,
            IMediaStorageService media,
            StripeGateway stripe,
            WhishGateway whish,
            IUnitOfWork uow,
            ILogger<EventService> logger)
        {
            _repo = repo;
            _regRepo = regRepo;
            _media = media;
            _stripe = stripe;
            _whish = whish;
            _uow = uow;
            _logger = logger;
        }

        // ── Admin ────────────────────────────────────────────────────────
        public async Task<IReadOnlyList<EventDto>> ListAsync(CancellationToken ct = default)
        {
            var events = await _repo.Query().OrderByDescending(e => e.Id).ToListAsync(ct);
            if (events.Count == 0) return Array.Empty<EventDto>();

            // One grouped query for the counters rather than N per event.
            var keys = events.Select(e => e.Id).ToList();
            var counts = await _regRepo.Query()
                .Where(r => r.EventId != null && keys.Contains(r.EventId.Value))
                .GroupBy(r => r.EventId!.Value)
                .Select(g => new
                {
                    EventId = g.Key,
                    Total = g.Count(),
                    Paid = g.Count(x => x.PaymentStatus == "Paid"),
                })
                .ToDictionaryAsync(x => x.EventId, ct);

            return events.Select(e =>
            {
                counts.TryGetValue(e.Id, out var c);
                return ToDto(e, c?.Total ?? 0, c?.Paid ?? 0);
            }).ToList();
        }

        public async Task<EventDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return null;
            var (total, paid) = await CountsAsync(e.Id, ct);
            return ToDto(e, total, paid);
        }

        public async Task<EventDto> CreateAsync(EventUpsertDto dto, string? actor, CancellationToken ct = default)
        {
            var key = Slugify(dto.Key);
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Event key (URL slug) is required.");
            if (string.IsNullOrWhiteSpace(dto.Title))
                throw new ArgumentException("Title is required.");

            if (await _repo.Query().AnyAsync(x => x.Key == key, ct))
                throw new InvalidOperationException($"An event with the key '{key}' already exists.");

            var e = new Event
            {
                Key = key,
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow,
            };
            ApplyUpsert(e, dto);

            await _repo.AddAsync(e, ct);
            await _uow.SaveChangesAsync(ct);
            InvalidatePublishedCache();
            _logger.LogInformation("Event '{Key}' created by {Actor}", key, actor ?? "system");
            return ToDto(e, 0, 0);
        }

        public async Task<EventDto> UpdateAsync(int id, EventUpsertDto dto, CancellationToken ct = default)
        {
            var e = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new KeyNotFoundException("Event not found.");

            var key = Slugify(dto.Key);
            if (!string.IsNullOrWhiteSpace(key) && key != e.Key)
            {
                if (await _repo.Query().AnyAsync(x => x.Key == key && x.Id != id, ct))
                    throw new InvalidOperationException($"An event with the key '{key}' already exists.");
                e.Key = key;
            }

            ApplyUpsert(e, dto);
            e.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            InvalidatePublishedCache();

            var (total, paid) = await CountsAsync(e.Id, ct);
            return ToDto(e, total, paid);
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var e = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return false;

            // Refuse to delete an event that already took money — the
            // registrations are financial records. Unpublish instead.
            var paid = await _regRepo.Query()
                .CountAsync(r => r.EventId == id && r.PaymentStatus == "Paid", ct);
            if (paid > 0)
                throw new InvalidOperationException(
                    $"Cannot delete: {paid} paid registration(s) exist. Unpublish the event instead.");

            // Best-effort media cleanup — a leftover file is harmless.
            try
            {
                await _media.DeleteAsync(e.VideoPath);
                await _media.DeleteAsync(e.HeroImagePath);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Media cleanup failed for event {Id}", id); }

            _repo.Remove(e);
            await _uow.SaveChangesAsync(ct);
            InvalidatePublishedCache();
            return true;
        }

        public async Task<bool> SetMediaAsync(int id, string? videoPath, string? heroImagePath, CancellationToken ct = default)
        {
            var e = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return false;

            if (videoPath != null)
            {
                // Replacing — drop the old file so uploads don't pile up.
                if (!string.IsNullOrWhiteSpace(e.VideoPath) && e.VideoPath != videoPath)
                    try { await _media.DeleteAsync(e.VideoPath); } catch { /* non-fatal */ }
                e.VideoPath = string.IsNullOrWhiteSpace(videoPath) ? null : videoPath;
            }

            if (heroImagePath != null)
            {
                if (!string.IsNullOrWhiteSpace(e.HeroImagePath) && e.HeroImagePath != heroImagePath)
                    try { await _media.DeleteAsync(e.HeroImagePath); } catch { /* non-fatal */ }
                e.HeroImagePath = string.IsNullOrWhiteSpace(heroImagePath) ? null : heroImagePath;
            }

            e.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            InvalidatePublishedCache();
            return true;
        }

        /// <summary>
        /// Events for the till screens: active, dated today or later (small
        /// grace window so an event that started an hour ago still shows).
        /// Includes drafts — cashiers need to see internally booked events
        /// even before they're published to the website.
        /// </summary>
        public async Task<IReadOnlyList<EventDto>> GetUpcomingAsync(int days = 21, CancellationToken ct = default)
        {
            var from = DateTime.UtcNow.AddHours(-12);
            var to = DateTime.UtcNow.AddDays(Math.Clamp(days, 1, 60));
            var all = await ListAsync(ct);
            return all
                .Where(e => e.IsActive && e.EventDate.HasValue
                         && e.EventDate.Value >= from && e.EventDate.Value <= to)
                .OrderBy(e => e.EventDate)
                .ToList();
        }

        /// <summary>
        /// Cashier-created event: minimal fields, auto-generated URL key,
        /// UNPUBLISHED — it appears on the internal boards immediately but
        /// never on the public website until an admin reviews and publishes.
        /// </summary>
        public async Task<EventDto> QuickCreateAsync(
            string title, string? type, DateTime? eventDate, string? location,
            decimal price, int? capacity, string? actor, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Title is required.");

            // Unique key: slug + date; on collision, add a counter.
            var baseKey = Slugify(title);
            if (string.IsNullOrWhiteSpace(baseKey)) baseKey = "event";
            if (eventDate.HasValue) baseKey += "-" + eventDate.Value.ToString("MMdd");

            var key = baseKey;
            for (var i = 2; await _repo.Query().AnyAsync(x => x.Key == key, ct); i++)
                key = $"{baseKey}-{i}";

            var dto = new EventUpsertDto(
                Key: key,
                Title: title.Trim(),
                Subtitle: null,
                Description: null,
                EventDate: eventDate,
                Location: location,
                Features: null,
                VideoYoutubeId: null,
                Price: price,
                Currency: "USD",
                EnableVisa: false,
                EnableWhish: false,
                EnableCash: true,          // in-store registration by default
                WhishPaymentLink: null,
                WhatsAppNumber: null,
                WhatsAppTemplate: null,
                IsPublished: false,        // admin publishes to the website
                IsActive: true,
                Capacity: capacity,
                Type: string.IsNullOrWhiteSpace(type) ? "Other" : type);

            return await CreateAsync(dto, actor, ct);
        }

        // ── Public ───────────────────────────────────────────────────────
        public async Task<EventPublicDto?> GetPublicAsync(string key, CancellationToken ct = default)
        {
            var e = await _repo.Query()
                .FirstOrDefaultAsync(x => x.Key == key && x.IsPublished && x.IsActive, ct);
            if (e is null) return null;

            // A method is offered only when the admin enabled it AND the
            // gateway actually has credentials. Cash needs no gateway.
            var stripeOk = await _stripe.IsConfiguredAsync(ct);
            var whishOk = await _whish.IsConfiguredAsync(ct);

            var paidCount = await _regRepo.Query()
                .CountAsync(r => r.EventId == e.Id && r.PaymentStatus == "Paid", ct);
            var soldOut = e.Capacity.HasValue && paidCount >= e.Capacity.Value;

            return new EventPublicDto(
                Key: e.Key,
                Title: e.Title,
                Subtitle: e.Subtitle,
                Description: e.Description,
                EventDate: e.EventDate,
                Location: e.Location,
                Features: ParseFeatures(e.FeaturesJson),
                VideoUrl: string.IsNullOrWhiteSpace(e.VideoPath) ? null : "/" + e.VideoPath.TrimStart('/'),
                VideoYoutubeId: e.VideoYoutubeId,
                HeroImageUrl: string.IsNullOrWhiteSpace(e.HeroImagePath) ? null : "/" + e.HeroImagePath.TrimStart('/'),
                Price: e.Price,
                Currency: e.Currency,
                VisaAvailable: e.EnableVisa && stripeOk,
                // Whish works either through the Collect API or through a
                // plain payment link — offer it if EITHER is available.
                WhishAvailable: e.EnableWhish && (whishOk || !string.IsNullOrWhiteSpace(e.WhishPaymentLink)),
                CashAvailable: e.EnableCash,
                IsSoldOut: soldOut);
        }

        // The website's event list rarely changes; keep it in memory briefly and
        // drop it whenever an admin edits an event.
        private static (DateTime At, IReadOnlyList<EventPublicSummaryDto> Items)? _publishedCache;
        private static readonly TimeSpan PublishedCacheTtl = TimeSpan.FromSeconds(60);
        private static void InvalidatePublishedCache() => _publishedCache = null;

        public async Task<IReadOnlyList<EventPublicSummaryDto>> GetPublishedAsync(CancellationToken ct = default)
        {
            var cached = _publishedCache;
            if (cached is not null && DateTime.UtcNow - cached.Value.At < PublishedCacheTtl)
                return cached.Value.Items;
            var items = await LoadPublishedAsync(ct);
            _publishedCache = (DateTime.UtcNow, items);
            return items;
        }

        private async Task<IReadOnlyList<EventPublicSummaryDto>> LoadPublishedAsync(CancellationToken ct)
        {
            // Same grace window as the till boards: an event that started a
            // few hours ago is still "on" for a visitor browsing the site.
            var from = DateTime.UtcNow.AddHours(-12);

            var events = await _repo.Query()
                .Where(x => x.IsPublished && x.IsActive
                         && (x.EventDate == null || x.EventDate >= from))
                .OrderBy(x => x.EventDate == null)
                .ThenBy(x => x.EventDate)
                .ThenBy(x => x.Title)
                .ToListAsync(ct);

            if (events.Count == 0) return Array.Empty<EventPublicSummaryDto>();

            var ids = events.Select(e => e.Id).ToList();
            var paidById = await _regRepo.Query()
                .Where(r => r.EventId != null && ids.Contains(r.EventId.Value) && r.PaymentStatus == "Paid")
                .GroupBy(r => r.EventId!.Value)
                .Select(g => new { EventId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.EventId, g => g.Count, ct);

            return events.Select(e =>
            {
                paidById.TryGetValue(e.Id, out var paid);
                return new EventPublicSummaryDto(
                    Key: e.Key,
                    Title: e.Title,
                    Subtitle: e.Subtitle,
                    EventDate: e.EventDate,
                    Location: e.Location,
                    Type: string.IsNullOrWhiteSpace(e.Type) ? "Other" : e.Type,
                    Price: e.Price,
                    Currency: e.Currency,
                    HeroImageUrl: string.IsNullOrWhiteSpace(e.HeroImagePath) ? null : "/" + e.HeroImagePath.TrimStart('/'),
                    Capacity: e.Capacity,
                    IsSoldOut: e.Capacity.HasValue && paid >= e.Capacity.Value);
            }).ToList();
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private async Task<(int total, int paid)> CountsAsync(int eventId, CancellationToken ct)
        {
            var rows = await _regRepo.Query()
                .Where(r => r.EventId == eventId)
                .Select(r => r.PaymentStatus)
                .ToListAsync(ct);
            return (rows.Count, rows.Count(s => s == "Paid"));
        }

        private static void ApplyUpsert(Event e, EventUpsertDto dto)
        {
            e.Title = dto.Title.Trim();
            e.Subtitle = dto.Subtitle?.Trim();
            e.Description = dto.Description?.Trim();
            e.EventDate = dto.EventDate.HasValue ? ForceUtc(dto.EventDate.Value) : null;
            e.Location = dto.Location?.Trim();
            e.FeaturesJson = dto.Features == null || dto.Features.Count == 0
                ? null
                : JsonSerializer.Serialize(dto.Features);
            e.VideoYoutubeId = ExtractYoutubeId(dto.VideoYoutubeId);
            e.Price = dto.Price < 0 ? 0 : dto.Price;
            e.Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency.Trim().ToUpperInvariant();
            e.EnableVisa = dto.EnableVisa;
            e.EnableWhish = dto.EnableWhish;
            e.EnableCash = dto.EnableCash;
            e.WhishPaymentLink = NormalizeLink(dto.WhishPaymentLink);
            e.WhatsAppNumber = string.IsNullOrWhiteSpace(dto.WhatsAppNumber)
                ? null
                : new string(dto.WhatsAppNumber.Where(char.IsDigit).ToArray());
            e.WhatsAppTemplate = string.IsNullOrWhiteSpace(dto.WhatsAppTemplate) ? null : dto.WhatsAppTemplate;
            e.IsPublished = dto.IsPublished;
            e.IsActive = dto.IsActive;
            e.Capacity = dto.Capacity is > 0 ? dto.Capacity : null;
            e.Type = string.IsNullOrWhiteSpace(dto.Type) ? "Other" : dto.Type.Trim();
        }

        private EventDto ToDto(Event e, int total, int paid) => new(
            e.Id, e.Key, e.Title, e.Subtitle, e.Description, e.EventDate, e.Location,
            ParseFeatures(e.FeaturesJson),
            e.VideoPath, e.VideoYoutubeId, e.HeroImagePath,
            e.Price, e.Currency,
            e.EnableVisa, e.EnableWhish, e.EnableCash,
            e.WhishPaymentLink,
            e.WhatsAppNumber, e.WhatsAppTemplate,
            e.IsPublished, e.IsActive, e.Capacity, e.CreatedOn,
            total, paid,
            Type: e.Type);

        private static List<EventFeatureDto> ParseFeatures(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<EventFeatureDto>();
            try
            {
                return JsonSerializer.Deserialize<List<EventFeatureDto>>(json, JsonOpts)
                       ?? new List<EventFeatureDto>();
            }
            catch { return new List<EventFeatureDto>(); }
        }

        /// <summary>
        /// Keeps a pasted Whish link usable: trims it and adds https:// when
        /// the admin pasted a bare host, so the anchor never resolves relative
        /// to our own domain.
        /// </summary>
        private static string? NormalizeLink(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim();
            if (!s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                s = "https://" + s;
            return s.Length > 500 ? s[..500] : s;
        }

        /// <summary>URL-safe slug: lowercase, spaces → dashes, strip the rest.</summary>
        private static string Slugify(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var lowered = raw.Trim().ToLowerInvariant().Replace(' ', '-');
            var kept = lowered.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_');
            return new string(kept.ToArray()).Trim('-');
        }

        /// <summary>
        /// Accepts a bare id or any common YouTube URL and returns the id,
        /// so the admin can paste whatever they copied from the address bar.
        /// </summary>
        private static string? ExtractYoutubeId(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var s = input.Trim();
            if (!s.Contains('/') && !s.Contains('?')) return s; // already an id

            try
            {
                var uri = new Uri(s.StartsWith("http") ? s : "https://" + s);
                if (uri.Host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                    return uri.AbsolutePath.Trim('/');

                var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var v = q["v"];
                if (!string.IsNullOrWhiteSpace(v)) return v;

                // /embed/{id} or /shorts/{id}
                var segs = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segs.Length >= 2 && (segs[0] == "embed" || segs[0] == "shorts")) return segs[1];
            }
            catch { /* fall through */ }
            return s;
        }

        private static DateTime ForceUtc(DateTime d) => d.Kind switch
        {
            DateTimeKind.Utc => d,
            DateTimeKind.Local => d.ToUniversalTime(),
            _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
        };
    }
}
