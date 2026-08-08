using System.Net.Http.Json;
using System.Text.Json;
using Application.IServices;
using Microsoft.Extensions.Logging;

namespace Application.Services.Payments
{
    /// <summary>
    /// Whish Money "Collect" hosted checkout (Lebanon).
    ///
    /// Whish authenticates with two headers — `channel` and `secret` — issued
    /// from the merchant dashboard, plus the registered `websiteurl`. The
    /// collect endpoint returns a `collectUrl` we redirect the buyer to; on
    /// completion Whish sends the buyer back to our successCallbackUrl and
    /// we verify the payment server-side before marking it Paid.
    ///
    /// `externalId` is our own reference (the registration id) — Whish echoes
    /// it back, and we also use it for the status re-check.
    ///
    /// NOTE: Whish's spec states request/response shapes may change. Every
    /// field is read defensively and any parse failure is logged with the
    /// raw body so the exact contract can be confirmed against the live
    /// account without redeploying.
    /// </summary>
    public class WhishGateway : IPaymentGateway
    {
        public string Method => "Whish";

        private readonly IIntegrationSettingsService _settings;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<WhishGateway> _logger;

        public WhishGateway(
            IIntegrationSettingsService settings,
            IHttpClientFactory httpFactory,
            ILogger<WhishGateway> logger)
        {
            _settings = settings;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
        {
            var channel = await _settings.GetRawAsync("Whish.Channel", ct);
            var secret = await _settings.GetRawAsync("Whish.Secret", ct);
            return !string.IsNullOrWhiteSpace(channel) && !string.IsNullOrWhiteSpace(secret);
        }

        public async Task<PaymentStartResult> StartAsync(
            int registrationId, decimal amount, string currency, string description,
            string customerName, string? customerEmail,
            string successUrl, string cancelUrl, CancellationToken ct = default)
        {
            var channel = await _settings.GetRawAsync("Whish.Channel", ct);
            var secret = await _settings.GetRawAsync("Whish.Secret", ct);
            var website = await _settings.GetRawAsync("Whish.WebsiteUrl", ct);
            var baseUrl = (await _settings.GetRawAsync("Whish.BaseUrl", ct))
                          ?? "https://whish.money/itel-service/api";

            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(secret))
                return new PaymentStartResult(false, null, null, "Whish is not configured.");

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Add("channel", channel);
            http.DefaultRequestHeaders.Add("secret", secret);
            if (!string.IsNullOrWhiteSpace(website))
                http.DefaultRequestHeaders.Add("websiteurl", website);

            // externalId must be unique per attempt. Registration id alone
            // would collide if the buyer retries after abandoning, so we
            // suffix a short timestamp.
            var externalId = $"{registrationId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            var body = new
            {
                amount,
                currency = currency.ToUpperInvariant(),
                invoice = description,
                externalId,
                successCallbackUrl = successUrl,
                failureCallbackUrl = cancelUrl,
                successRedirectUrl = successUrl,
                failureRedirectUrl = cancelUrl,
            };

            try
            {
                var resp = await http.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}/payment/whish", body, ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("Whish collect failed ({Code}): {Body}", (int)resp.StatusCode, raw);
                    return new PaymentStartResult(false, null, null, $"Whish error {(int)resp.StatusCode}");
                }

                // Expected: { status: true, code: "...", data: { collectUrl: "..." } }
                using var jd = JsonDocument.Parse(raw);
                var root = jd.RootElement;

                var ok = !root.TryGetProperty("status", out var st)
                         || st.ValueKind != JsonValueKind.False;
                if (!ok)
                {
                    var msg = root.TryGetProperty("dialog", out var dlg)
                              && dlg.TryGetProperty("message", out var m)
                        ? m.GetString() : "Whish rejected the request.";
                    _logger.LogError("Whish collect rejected: {Body}", raw);
                    return new PaymentStartResult(false, null, null, msg);
                }

                string? collectUrl = null;
                if (root.TryGetProperty("data", out var data))
                {
                    if (data.TryGetProperty("collectUrl", out var cu)) collectUrl = cu.GetString();
                    else if (data.TryGetProperty("url", out var u)) collectUrl = u.GetString();
                }

                if (string.IsNullOrWhiteSpace(collectUrl))
                {
                    _logger.LogError("Whish collect returned no URL. Raw: {Body}", raw);
                    return new PaymentStartResult(false, null, null, "Whish did not return a payment URL.");
                }

                return new PaymentStartResult(true, collectUrl, externalId, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Whish collect threw for registration {Id}", registrationId);
                return new PaymentStartResult(false, null, null, ex.Message);
            }
        }

        /// <summary>
        /// Server-side verification after Whish redirects the buyer back.
        /// Never trust the redirect alone — always re-ask Whish whether the
        /// collect actually succeeded before marking the row Paid.
        /// </summary>
        public async Task<bool> VerifyAsync(string externalId, CancellationToken ct = default)
        {
            var channel = await _settings.GetRawAsync("Whish.Channel", ct);
            var secret = await _settings.GetRawAsync("Whish.Secret", ct);
            var website = await _settings.GetRawAsync("Whish.WebsiteUrl", ct);
            var baseUrl = (await _settings.GetRawAsync("Whish.BaseUrl", ct))
                          ?? "https://whish.money/itel-service/api";

            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(secret)) return false;

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Add("channel", channel);
            http.DefaultRequestHeaders.Add("secret", secret);
            if (!string.IsNullOrWhiteSpace(website))
                http.DefaultRequestHeaders.Add("websiteurl", website);

            try
            {
                var resp = await http.PostAsJsonAsync(
                    $"{baseUrl.TrimEnd('/')}/payment/collect/status",
                    new { externalId }, ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Whish status check failed ({Code}) for {Ext}: {Body}",
                        (int)resp.StatusCode, externalId, raw);
                    return false;
                }

                using var jd = JsonDocument.Parse(raw);
                if (jd.RootElement.TryGetProperty("data", out var data)
                    && data.TryGetProperty("collectStatus", out var cs))
                {
                    var status = cs.GetString();
                    return string.Equals(status, "success", StringComparison.OrdinalIgnoreCase);
                }

                _logger.LogWarning("Whish status check: unexpected shape for {Ext}: {Body}", externalId, raw);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Whish status check threw for {Ext}", externalId);
                return false;
            }
        }
    }
}
