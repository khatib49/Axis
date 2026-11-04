using System.Net.Http.Json;
using System.Text.Json;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Phase5
{
    public record NotifyResult(int Sent, int Failed, List<string> Notes);

    /// <summary>
    /// Sends short text messages to the owner's phone: Telegram (bot), WhatsApp
    /// (Cloud API text, works inside the 24-hour window; outside it Meta needs a
    /// template, set Alerts.WhatsAppTemplate with one body parameter), email,
    /// and an in-app notification for admins.
    /// Default recipients: OwnerBot.AllowedTelegramChatIds, OwnerBot.AllowedWhatsAppNumbers, Alerts.Emails.
    /// </summary>
    public interface IOwnerNotifier
    {
        Task<NotifyResult> NotifyOwnersAsync(string title, string text, IEnumerable<string> channels, string? extraRecipients = null, CancellationToken ct = default);
        Task<bool> SendTelegramAsync(string chatId, string text, CancellationToken ct = default);
        Task<bool> SendWhatsAppTextAsync(string phone, string text, CancellationToken ct = default);
    }

    public class OwnerNotifier : IOwnerNotifier
    {
        public const string TelegramTokenKey = "Telegram.BotToken";
        public const string AllowedTelegramKey = "OwnerBot.AllowedTelegramChatIds";
        public const string AllowedWhatsAppKey = "OwnerBot.AllowedWhatsAppNumbers";
        public const string AlertEmailsKey = "Alerts.Emails";
        public const string WhatsAppTemplateKey = "Alerts.WhatsAppTemplate";

        private readonly ApplicationDbContext _db;
        private readonly IIntegrationSettingsService _settings;
        private readonly IHttpClientFactory _http;
        private readonly IEmailSender _email;
        private readonly ILogger<OwnerNotifier> _log;

        public OwnerNotifier(ApplicationDbContext db, IIntegrationSettingsService settings, IHttpClientFactory http, IEmailSender email, ILogger<OwnerNotifier> log)
        { _db = db; _settings = settings; _http = http; _email = email; _log = log; }

        public static List<string> SplitList(string? s) => (s ?? "").Split(new[] { ',', ';', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList();

        public async Task<NotifyResult> NotifyOwnersAsync(string title, string text, IEnumerable<string> channels, string? extraRecipients = null, CancellationToken ct = default)
        {
            var chans = channels.Select(c => c.Trim()).Where(c => c.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var extra = SplitList(extraRecipients);
            int sent = 0, failed = 0; var notes = new List<string>();
            var full = $"{title}\n{text}";

            if (chans.Contains("Telegram"))
            {
                var ids = SplitList(await _settings.GetRawAsync(AllowedTelegramKey, ct)).Concat(extra.Where(e => e.All(c => char.IsDigit(c) || c == '-') && e.Length < 16)).Distinct().ToList();
                if (ids.Count == 0) notes.Add("Telegram: no chat ids");
                foreach (var id in ids) { if (await SendTelegramAsync(id, full, ct)) sent++; else failed++; }
            }
            if (chans.Contains("WhatsApp"))
            {
                var phones = SplitList(await _settings.GetRawAsync(AllowedWhatsAppKey, ct)).Concat(extra.Where(e => e.StartsWith('+'))).Distinct().ToList();
                if (phones.Count == 0) notes.Add("WhatsApp: no numbers");
                foreach (var p in phones) { if (await SendWhatsAppTextAsync(p, full, ct)) sent++; else failed++; }
            }
            if (chans.Contains("Email"))
            {
                var emails = SplitList(await _settings.GetRawAsync(AlertEmailsKey, ct)).Concat(extra.Where(e => e.Contains('@'))).Distinct().ToList();
                if (emails.Count == 0) notes.Add("Email: no addresses");
                else
                {
                    var r = await _email.SendAsync(emails, title, text, false, null, ct);
                    if (r.Sent) sent += emails.Count; else { failed += emails.Count; notes.Add("Email: " + (r.Error ?? "not configured")); }
                }
            }
            if (chans.Contains("InApp"))
            {
                var admins = await (from u in _db.Users join ur in _db.UserRoles on u.Id equals ur.UserId join r in _db.Roles on ur.RoleId equals r.Id
                                    where r.Name == "admin" select u.Id).Distinct().ToListAsync(ct);
                foreach (var a in admins) _db.Add(new Notification { UserId = a, Title = Trunc(title, 200), Body = Trunc(text, 1000), Type = "Alert", CreatedOn = DateTime.UtcNow });
                await _db.SaveChangesAsync(ct);
                sent += admins.Count;
            }
            return new(sent, failed, notes);
        }

        private static string Trunc(string s, int n) => s.Length > n ? s[..n] : s;

        public async Task<bool> SendTelegramAsync(string chatId, string text, CancellationToken ct = default)
        {
            var token = await _settings.GetRawAsync(TelegramTokenKey, ct);
            if (string.IsNullOrWhiteSpace(token)) { _log.LogInformation("Telegram not configured; message to {Chat} not sent", chatId); return false; }
            try
            {
                using var http = _http.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                var baseUrl = (await _settings.GetRawAsync("Telegram.ApiBaseUrl", ct))?.TrimEnd('/') ?? "https://api.telegram.org";
                using var resp = await http.PostAsJsonAsync($"{baseUrl}/bot{token.Trim()}/sendMessage", new { chat_id = chatId, text = Trunc(text, 4000) }, ct);
                if (!resp.IsSuccessStatusCode) _log.LogWarning("Telegram sendMessage to {Chat} failed: HTTP {Code}", chatId, (int)resp.StatusCode);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { _log.LogWarning(ex, "Telegram send failed"); return false; }
        }

        public async Task<bool> SendWhatsAppTextAsync(string phone, string text, CancellationToken ct = default)
        {
            var phoneId = await _settings.GetRawAsync("WhatsApp.PhoneNumberId", ct);
            var token = await _settings.GetRawAsync("WhatsApp.AccessToken", ct);
            if (string.IsNullOrWhiteSpace(phoneId) || string.IsNullOrWhiteSpace(token)) { _log.LogInformation("WhatsApp not configured; message to {Phone} not sent", phone); return false; }
            var to = new string(phone.Where(char.IsDigit).ToArray());
            var template = await _settings.GetRawAsync(WhatsAppTemplateKey, ct);
            object body = string.IsNullOrWhiteSpace(template)
                ? new { messaging_product = "whatsapp", to, type = "text", text = new { body = Trunc(text, 4000) } }
                : new
                {
                    messaging_product = "whatsapp", to, type = "template",
                    template = new { name = template.Trim(), language = new { code = (await _settings.GetRawAsync("Alerts.WhatsAppLanguage", ct)) ?? "en" },
                        components = new[] { new { type = "body", parameters = new[] { new { type = "text", text = Trunc(text.Replace('\n', ' '), 900) } } } } },
                };
            try
            {
                using var http = _http.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(15);
                http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token.Trim());
                var baseUrl = (await _settings.GetRawAsync("WhatsApp.ApiBaseUrl", ct))?.TrimEnd('/') ?? "https://graph.facebook.com/v19.0";
                using var resp = await http.PostAsJsonAsync($"{baseUrl}/{phoneId.Trim()}/messages", body, ct);
                return resp.IsSuccessStatusCode;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { _log.LogWarning(ex, "WhatsApp send failed"); return false; }
        }
    }

    /// <summary>Venue day / time helpers (Asia/Beirut unless Venue.TimeZone is set).</summary>
    public static class VenueClock
    {
        public static TimeZoneInfo Zone(string? id)
        {
            foreach (var z in new[] { id, "Asia/Beirut", "Middle East Standard Time" })
            {
                if (string.IsNullOrWhiteSpace(z)) continue;
                try { return TimeZoneInfo.FindSystemTimeZoneById(z); } catch (TimeZoneNotFoundException) { } catch (InvalidTimeZoneException) { }
            }
            return TimeZoneInfo.Utc;
        }
        /// <summary>UTC start of the venue's current day.</summary>
        public static DateTime TodayStartUtc(TimeZoneInfo tz, DateTime? nowUtc = null)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc ?? DateTime.UtcNow, tz);
            return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified), tz);
        }
        public static int LocalHour(TimeZoneInfo tz, DateTime? nowUtc = null) => TimeZoneInfo.ConvertTimeFromUtc(nowUtc ?? DateTime.UtcNow, tz).Hour;
    }
}
