using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;

namespace Application.Services.Ai
{
    /// <summary>
    /// Meta WhatsApp Cloud API client.
    ///
    /// Outbound messages to anyone outside the 24-hour customer service
    /// window MUST use a pre-approved template (Meta requirement). We send
    /// the template name + parameter list — Meta substitutes them on
    /// their side.
    ///
    /// Each recipient becomes one POST to /{phoneId}/messages. We don't
    /// parallel-blast to keep Meta rate limits happy and to make per-message
    /// logging clean.
    /// </summary>
    public class WhatsAppService : IWhatsAppService
    {
        private readonly IIntegrationSettingsService _settings;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ApplicationDbContext _db;

        public WhatsAppService(
            IIntegrationSettingsService settings,
            IHttpClientFactory httpFactory,
            ApplicationDbContext db)
        {
            _settings = settings;
            _httpFactory = httpFactory;
            _db = db;
        }

        public async Task<WhatsAppBlastResult> SendBlastAsync(
            int? pendingActionId,
            IEnumerable<WhatsAppRecipient> recipients,
            string templateName,
            IEnumerable<string> templateParams,
            CancellationToken ct = default)
        {
            var phoneId = await _settings.GetRawAsync("WhatsApp.PhoneNumberId", ct)
                ?? throw new InvalidOperationException("WhatsApp.PhoneNumberId not set");
            var token = await _settings.GetRawAsync("WhatsApp.AccessToken", ct)
                ?? throw new InvalidOperationException("WhatsApp.AccessToken not set");

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            int sent = 0, failed = 0;
            var errors = new List<string>();
            var baseParams = templateParams?.ToList() ?? new List<string>();

            foreach (var r in recipients)
            {
                ct.ThrowIfCancellationRequested();

                var msgRow = new WhatsAppMessage
                {
                    PendingActionId = pendingActionId,
                    RecipientPhone = NormalisePhone(r.Phone),
                    RecipientName = r.Name,
                    TemplateName = templateName,
                    Status = "Queued",
                    QueuedOn = DateTime.UtcNow
                };
                _db.WhatsAppMessages.Add(msgRow);
                await _db.SaveChangesAsync(ct);

                try
                {
                    // Per-recipient params override the base if provided.
                    var paramList = (r.PerRecipientParams?.ToList() ?? baseParams);
                    msgRow.MessageBody = $"[{templateName}] " + string.Join(" | ", paramList);

                    var body = new
                    {
                        messaging_product = "whatsapp",
                        to = msgRow.RecipientPhone,
                        type = "template",
                        template = new
                        {
                            name = templateName,
                            language = new { code = "en_US" },
                            components = paramList.Count == 0 ? null : new object[]
                            {
                                new {
                                    type = "body",
                                    parameters = paramList.Select(p => new { type = "text", text = p }).ToArray()
                                }
                            }
                        }
                    };

                    var resp = await http.PostAsJsonAsync(
                        $"https://graph.facebook.com/v19.0/{phoneId}/messages", body, ct);
                    var raw = await resp.Content.ReadAsStringAsync(ct);

                    if (resp.IsSuccessStatusCode)
                    {
                        msgRow.Status = "Sent";
                        msgRow.SentOn = DateTime.UtcNow;
                        try
                        {
                            using var jd = JsonDocument.Parse(raw);
                            if (jd.RootElement.TryGetProperty("messages", out var arr) && arr.GetArrayLength() > 0)
                                msgRow.ProviderMessageId = arr[0].GetProperty("id").GetString();
                        }
                        catch { /* keep status=Sent even if id missing */ }
                        sent++;
                    }
                    else
                    {
                        msgRow.Status = "Failed";
                        msgRow.ErrorMessage = Truncate(raw, 1000);
                        errors.Add($"{msgRow.RecipientPhone}: HTTP {(int)resp.StatusCode}");
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    msgRow.Status = "Failed";
                    msgRow.ErrorMessage = ex.Message;
                    errors.Add($"{msgRow.RecipientPhone}: {ex.Message}");
                    failed++;
                }

                _db.WhatsAppMessages.Update(msgRow);
                await _db.SaveChangesAsync(ct);
            }

            return new WhatsAppBlastResult(sent, failed, errors);
        }

        private static string NormalisePhone(string raw)
        {
            // Strip everything but digits + leading '+'.
            var trimmed = (raw ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed)) return "";
            var digits = new string(trimmed.Where(c => char.IsDigit(c) || c == '+').ToArray());
            return digits;
        }

        private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
    }
}
