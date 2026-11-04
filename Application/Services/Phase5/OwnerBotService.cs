using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Phase5
{
    public interface IOwnerBotService
    {
        Task<OwnerBotStatusDto> StatusAsync(CancellationToken ct = default);
        Task<BaseResponse<OwnerBotChatDto>> SetAllowedAsync(int chatId, bool allowed, CancellationToken ct = default);
        Task<bool> CheckTelegramSecretAsync(string? secret, CancellationToken ct = default);
        Task HandleTelegramAsync(string rawJson, CancellationToken ct = default);
        Task<string?> VerifyWhatsAppAsync(string? mode, string? verifyToken, string? challenge, CancellationToken ct = default);
        Task<bool> CheckWhatsAppSignatureAsync(string? signature, string rawBody, CancellationToken ct = default);
        Task HandleWhatsAppAsync(string rawJson, CancellationToken ct = default);
        Task<BaseResponse<string>> SetTelegramWebhookAsync(string publicBaseUrl, CancellationToken ct = default);
        Task<string> SummaryTextAsync(CancellationToken ct = default);
        Task<string> AnswerAsync(string channel, string chatId, string? name, string text, CancellationToken ct = default);
        Task<NotifyResult> SendDailySummaryAsync(bool force, CancellationToken ct = default);
    }

    /// <summary>
    /// The owner's assistant on Telegram and WhatsApp. Only allowed chats get answers
    /// (OwnerBot.AllowedTelegramChatIds / OwnerBot.AllowedWhatsAppNumbers, or allowed in the dashboard).
    /// /summary answers without AI; any other question goes to the Axis AI assistant
    /// (same tools and safety gate as the dashboard chat; write actions stay pending for approval).
    /// </summary>
    public class OwnerBotService : IOwnerBotService
    {
        public const string TelegramSecretKey = "Telegram.WebhookSecret";
        public const string WhatsAppVerifyKey = "WhatsApp.VerifyToken";
        public const string WhatsAppAppSecretKey = "WhatsApp.AppSecret";
        public const string DailyHourKey = "OwnerBot.DailySummaryHour";
        private const int PaidStatus = 6;

        private readonly ApplicationDbContext _db;
        private readonly IIntegrationSettingsService _settings;
        private readonly IOwnerNotifier _notify;
        private readonly IAiChatService _ai;
        private readonly IHttpClientFactory _http;
        private readonly ILogger<OwnerBotService> _log;

        public OwnerBotService(ApplicationDbContext db, IIntegrationSettingsService settings, IOwnerNotifier notify, IAiChatService ai, IHttpClientFactory http, ILogger<OwnerBotService> log)
        { _db = db; _settings = settings; _notify = notify; _ai = ai; _http = http; _log = log; }

        public async Task<OwnerBotStatusDto> StatusAsync(CancellationToken ct = default)
        {
            var chats = await _db.Set<OwnerBotChat>().AsNoTracking().OrderByDescending(c => c.LastMessageOn).Take(100).ToListAsync(ct);
            var allowed = await AllowedAsync(ct);
            return new(!string.IsNullOrWhiteSpace(await _settings.GetRawAsync(OwnerNotifier.TelegramTokenKey, ct)),
                !string.IsNullOrWhiteSpace(await _settings.GetRawAsync("WhatsApp.AccessToken", ct)) && !string.IsNullOrWhiteSpace(await _settings.GetRawAsync("WhatsApp.PhoneNumberId", ct)),
                !string.IsNullOrWhiteSpace(await _settings.GetRawAsync("Anthropic.ApiKey", ct)),
                "/api/owner-bot/telegram", "/api/owner-bot/whatsapp", await DailyHourAsync(ct),
                chats.Select(c => new OwnerBotChatDto(c.Id, c.Channel, c.ChatId, c.DisplayName, c.IsAllowed || allowed.Contains((c.Channel, c.ChatId)), c.FirstSeenOn, c.LastMessageOn)).ToList());
        }

        private async Task<int> DailyHourAsync(CancellationToken ct) => int.TryParse(await _settings.GetRawAsync(DailyHourKey, ct), out var h) && h is >= 0 and <= 23 ? h : 23;

        private async Task<HashSet<(string, string)>> AllowedAsync(CancellationToken ct)
        {
            var set = new HashSet<(string, string)>();
            foreach (var id in OwnerNotifier.SplitList(await _settings.GetRawAsync(OwnerNotifier.AllowedTelegramKey, ct))) set.Add(("Telegram", id));
            foreach (var p in OwnerNotifier.SplitList(await _settings.GetRawAsync(OwnerNotifier.AllowedWhatsAppKey, ct))) set.Add(("WhatsApp", Digits(p)));
            return set;
        }

        private static string Digits(string s) => new(s.Where(char.IsDigit).ToArray());

        public async Task<BaseResponse<OwnerBotChatDto>> SetAllowedAsync(int chatId, bool allowed, CancellationToken ct = default)
        {
            var c = await _db.Set<OwnerBotChat>().FirstOrDefaultAsync(x => x.Id == chatId, ct);
            if (c is null) return new(false, "NotFound", "Chat not found.");
            c.IsAllowed = allowed;
            var key = c.Channel == "Telegram" ? OwnerNotifier.AllowedTelegramKey : OwnerNotifier.AllowedWhatsAppKey;
            var list = OwnerNotifier.SplitList(await _settings.GetRawAsync(key, ct));
            var id = c.Channel == "WhatsApp" ? "+" + c.ChatId : c.ChatId;
            list.RemoveAll(x => (c.Channel == "WhatsApp" ? Digits(x) : x) == c.ChatId);
            if (allowed) list.Add(id);
            await _settings.UpsertAsync(key, string.Join(",", list), "owner-bot", ct);
            await _db.SaveChangesAsync(ct);
            return new(true, null, allowed ? "Chat allowed." : "Chat blocked.", new(c.Id, c.Channel, c.ChatId, c.DisplayName, allowed, c.FirstSeenOn, c.LastMessageOn));
        }

        public async Task<bool> CheckTelegramSecretAsync(string? secret, CancellationToken ct = default)
        {
            var expected = await _settings.GetRawAsync(TelegramSecretKey, ct);
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrEmpty(secret)) return false;
            var a = Encoding.UTF8.GetBytes(expected.Trim()); var b = Encoding.UTF8.GetBytes(secret.Trim());
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        public async Task HandleTelegramAsync(string rawJson, CancellationToken ct = default)
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (!doc.RootElement.TryGetProperty("message", out var msg)) return;
            if (!msg.TryGetProperty("chat", out var chat) || !chat.TryGetProperty("id", out var idEl)) return;
            var chatId = idEl.ToString();
            var text = msg.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var name = msg.TryGetProperty("from", out var from) && from.TryGetProperty("first_name", out var fn) ? fn.GetString() : null;
            var reply = await AnswerAsync("Telegram", chatId, name, text, ct);
            await _notify.SendTelegramAsync(chatId, reply, ct);
        }

        public async Task<string?> VerifyWhatsAppAsync(string? mode, string? verifyToken, string? challenge, CancellationToken ct = default)
        {
            var expected = await _settings.GetRawAsync(WhatsAppVerifyKey, ct);
            return mode == "subscribe" && !string.IsNullOrWhiteSpace(expected) && verifyToken == expected.Trim() ? challenge : null;
        }

        public async Task<bool> CheckWhatsAppSignatureAsync(string? signature, string rawBody, CancellationToken ct = default)
        {
            var secret = await _settings.GetRawAsync(WhatsAppAppSecretKey, ct);
            if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(signature)) return false;
            var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret.Trim()), Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
            var a = Encoding.UTF8.GetBytes(expected); var b = Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant());
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        public async Task HandleWhatsAppAsync(string rawJson, CancellationToken ct = default)
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (!doc.RootElement.TryGetProperty("entry", out var entries)) return;
            foreach (var e in entries.EnumerateArray())
            {
                if (!e.TryGetProperty("changes", out var changes)) continue;
                foreach (var c in changes.EnumerateArray())
                {
                    if (!c.TryGetProperty("value", out var v) || !v.TryGetProperty("messages", out var msgs)) continue;
                    string? name = v.TryGetProperty("contacts", out var contacts) && contacts.GetArrayLength() > 0 && contacts[0].TryGetProperty("profile", out var prof) && prof.TryGetProperty("name", out var nm) ? nm.GetString() : null;
                    foreach (var m in msgs.EnumerateArray())
                    {
                        var fromNum = m.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "";
                        var text = m.TryGetProperty("text", out var t) && t.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                        if (fromNum.Length == 0) continue;
                        var reply = await AnswerAsync("WhatsApp", Digits(fromNum), name, text, ct);
                        await _notify.SendWhatsAppTextAsync("+" + Digits(fromNum), reply, ct);
                    }
                }
            }
        }

        public async Task<string> AnswerAsync(string channel, string chatId, string? name, string text, CancellationToken ct = default)
        {
            var chat = await _db.Set<OwnerBotChat>().FirstOrDefaultAsync(c => c.Channel == channel && c.ChatId == chatId, ct);
            if (chat is null) { chat = new OwnerBotChat { Channel = channel, ChatId = chatId, DisplayName = name }; _db.Add(chat); }
            chat.LastMessageOn = DateTime.UtcNow; chat.DisplayName ??= name;
            await _db.SaveChangesAsync(ct);
            var allowed = chat.IsAllowed || (await AllowedAsync(ct)).Contains((channel, chatId));
            if (!allowed)
                return $"Hello{(name is null ? "" : " " + name)}. This is the Axis owner assistant. Your {(channel == "Telegram" ? "chat id" : "number")} is {chatId}. Ask the admin to allow it in Axis → Owner Assistant.";

            var q = (text ?? "").Trim();
            var cmd = q.ToLowerInvariant();
            if (cmd is "" or "/start" or "/help" or "help" or "مساعدة")
                return "Axis owner assistant.\n/summary or ملخص: today's numbers.\nOr ask anything, in English or Arabic, e.g. \"How much did we sell today?\" or \"كم بعنا اليوم؟\"";
            if (cmd is "/summary" or "summary" or "ملخص" or "/ملخص") return await SummaryTextAsync(ct);
            if (string.IsNullOrWhiteSpace(await _settings.GetRawAsync("Anthropic.ApiKey", ct)))
                return await SummaryTextAsync(ct) + "\n\n(Set the AI key in Integrations to ask free questions.)";
            try
            {
                var prompt = $"[{channel} message from the owner{(name is null ? "" : " " + name)}. Answer briefly for a phone screen, in the same language as the question. No markdown tables.]\n{q}";
                var r = await _ai.SendAsync(new AiSendMessageRequest(chat.ConversationId, prompt), $"{channel.ToLowerInvariant()}:{chatId}", ct);
                chat.ConversationId = r.ConversationId;
                await _db.SaveChangesAsync(ct);
                var answer = string.Join("\n", r.NewMessages.Where(m => m.Role == "assistant" && !string.IsNullOrWhiteSpace(m.Content)).Select(m => m.Content!.Trim()));
                if (r.ProposedActionIds.Count > 0) answer += $"\n\n{r.ProposedActionIds.Count} action(s) wait for your approval in the Axis dashboard.";
                return string.IsNullOrWhiteSpace(answer) ? "Done." : answer;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Owner bot AI answer failed");
                return "The AI assistant could not answer right now.\n\n" + await SummaryTextAsync(ct);
            }
        }

        public async Task<string> SummaryTextAsync(CancellationToken ct = default)
        {
            var tz = VenueClock.Zone(await _settings.GetRawAsync("Venue.TimeZone", ct));
            var start = VenueClock.TodayStartUtc(tz);
            var paid = _db.Set<TransactionRecord>().AsNoTracking().Where(t => t.StatusId == PaidStatus && (t.PaidOn ?? t.CreatedOn) >= start);
            var total = await paid.SumAsync(t => (decimal?)t.TotalPrice, ct) ?? 0;
            var count = await paid.CountAsync(ct);
            var games = await paid.Where(t => t.GameId != null).SumAsync(t => (decimal?)t.TotalPrice, ct) ?? 0;
            var open = await _db.Set<TransactionRecord>().AsNoTracking().CountAsync(t => t.StatusId == 7, ct);
            var running = (await StationBusy.RunningSetIdsAsync(_db, ct)).Count;
            var voids = await _db.Set<VoidRequest>().AsNoTracking().CountAsync(v => v.Status == "Pending", ct);
            var low = await _db.Set<Ingredient>().AsNoTracking().CountAsync(i => i.IsActive && i.ReorderLevel != null && i.QuantityOnHand <= i.ReorderLevel, ct);
            var top = await (from ti in _db.Set<TransactionItem>().AsNoTracking()
                             join t in paid on ti.TransactionRecordId equals t.Id
                             group ti by ti.Item.Name into g
                             orderby g.Sum(x => x.Quantity) descending
                             select new { Name = g.Key, Q = g.Sum(x => x.Quantity) }).Take(3).ToListAsync(ct);
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            var sb = new StringBuilder();
            sb.AppendLine($"Axis · {local:ddd dd MMM HH:mm}");
            sb.AppendLine($"Sales today: ${total:0.00} ({count} paid invoices, games ${games:0.00})");
            sb.AppendLine($"Open invoices: {open} · Stations playing: {running}");
            if (voids > 0) sb.AppendLine($"Void requests waiting: {voids}");
            if (low > 0) sb.AppendLine($"Low stock items: {low}");
            if (top.Count > 0) sb.AppendLine("Top: " + string.Join(", ", top.Select(x => $"{x.Name} ×{x.Q}")));
            return sb.ToString().TrimEnd();
        }

        public async Task<BaseResponse<string>> SetTelegramWebhookAsync(string publicBaseUrl, CancellationToken ct = default)
        {
            var token = await _settings.GetRawAsync(OwnerNotifier.TelegramTokenKey, ct);
            if (string.IsNullOrWhiteSpace(token)) return new(false, "NotConfigured", "Set Telegram.BotToken first.");
            var secret = await _settings.GetRawAsync(TelegramSecretKey, ct);
            if (string.IsNullOrWhiteSpace(secret)) { secret = TournamentService.NewToken(); await _settings.UpsertAsync(TelegramSecretKey, secret, "owner-bot", ct); }
            var url = publicBaseUrl.TrimEnd('/') + "/api/owner-bot/telegram";
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return new(false, "Validation", "Telegram needs a public https address for the API.");
            try
            {
                using var http = _http.CreateClient();
                var baseUrl = (await _settings.GetRawAsync("Telegram.ApiBaseUrl", ct))?.TrimEnd('/') ?? "https://api.telegram.org";
                using var resp = await http.PostAsJsonAsync($"{baseUrl}/bot{token.Trim()}/setWebhook", new { url, secret_token = secret.Trim(), allowed_updates = new[] { "message" } }, ct);
                var body = await resp.Content.ReadAsStringAsync(ct);
                return resp.IsSuccessStatusCode ? new(true, null, "Telegram webhook set.", url) : new(false, "Provider", $"Telegram refused: {body}");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { return new(false, "Provider", ex.Message); }
        }

        public async Task<NotifyResult> SendDailySummaryAsync(bool force, CancellationToken ct = default)
        {
            var tz = VenueClock.Zone(await _settings.GetRawAsync("Venue.TimeZone", ct));
            if (!force && VenueClock.LocalHour(tz) != await DailyHourAsync(ct)) return new(0, 0, new() { "Not the summary hour." });
            if (!force && string.Equals((await _settings.GetRawAsync("OwnerBot.DailySummary", ct))?.Trim(), "false", StringComparison.OrdinalIgnoreCase)) return new(0, 0, new() { "Daily summary is off." });
            return await _notify.NotifyOwnersAsync("Daily summary", await SummaryTextAsync(ct), new[] { "Telegram", "WhatsApp" }, null, ct);
        }
    }

    public class OwnerBotJobs
    {
        private readonly IOwnerBotService _svc;
        public OwnerBotJobs(IOwnerBotService svc) { _svc = svc; }
        public Task DailySummaryAsync(CancellationToken ct) => _svc.SendDailySummaryAsync(false, ct);
    }
}
