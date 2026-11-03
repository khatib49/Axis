using System.Net.Http.Json;
using System.Text.Json;
using Application.IServices;
using Microsoft.Extensions.Logging;

namespace Application.Services.Payments2
{
    public record WhishCollectResult(bool Success, string? CollectUrl, string? Error, string? Raw = null);
    /// <summary>Provider status: "success" | "pending" | "failed" (null when the call itself failed).</summary>
    public record WhishCollectStatus(string? Status, string? Raw);

    /// <summary>
    /// Minimal Whish Collect HTTP client for invoice payments. The event
    /// gateway (Application.Services.Payments.WhishGateway) is coupled to an
    /// event registration id, so the two calls we need — create a collect and
    /// read its status — are duplicated here against the same
    /// IntegrationSettings keys (Whish.Channel, Whish.Secret, Whish.WebsiteUrl,
    /// Whish.BaseUrl). Behind an interface so services can be unit-tested.
    /// </summary>
    public interface IWhishCollectClient
    {
        Task<bool> IsConfiguredAsync(CancellationToken ct = default);
        Task<WhishCollectResult> CreateCollectAsync(string externalId, decimal amount, string currency, string description, string successUrl, string failureUrl, CancellationToken ct = default);
        Task<WhishCollectStatus> GetStatusAsync(string externalId, CancellationToken ct = default);
    }

    public class WhishCollectClient : IWhishCollectClient
    {
        private const string DefaultBaseUrl = "https://whish.money/itel-service/api";
        private readonly IIntegrationSettingsService _settings;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<WhishCollectClient> _logger;

        public WhishCollectClient(IIntegrationSettingsService settings, IHttpClientFactory httpFactory, ILogger<WhishCollectClient> logger)
        {
            _settings = settings;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        private async Task<(HttpClient? http, string baseUrl)> ClientAsync(CancellationToken ct)
        {
            var channel = await _settings.GetRawAsync("Whish.Channel", ct);
            var secret = await _settings.GetRawAsync("Whish.Secret", ct);
            var website = await _settings.GetRawAsync("Whish.WebsiteUrl", ct);
            var baseUrl = (await _settings.GetRawAsync("Whish.BaseUrl", ct)) ?? DefaultBaseUrl;
            if (string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(secret)) return (null, baseUrl);

            var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Add("channel", channel);
            http.DefaultRequestHeaders.Add("secret", secret);
            if (!string.IsNullOrWhiteSpace(website)) http.DefaultRequestHeaders.Add("websiteurl", website);
            return (http, baseUrl.TrimEnd('/'));
        }

        public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
        {
            var channel = await _settings.GetRawAsync("Whish.Channel", ct);
            var secret = await _settings.GetRawAsync("Whish.Secret", ct);
            return !string.IsNullOrWhiteSpace(channel) && !string.IsNullOrWhiteSpace(secret);
        }

        public async Task<WhishCollectResult> CreateCollectAsync(string externalId, decimal amount, string currency, string description, string successUrl, string failureUrl, CancellationToken ct = default)
        {
            var (http, baseUrl) = await ClientAsync(ct);
            if (http is null) return new WhishCollectResult(false, null, "Whish is not configured.");
            using (http)
            {
                var body = new
                {
                    amount,
                    currency = currency.ToUpperInvariant(),
                    invoice = description,
                    externalId,
                    successCallbackUrl = successUrl,
                    failureCallbackUrl = failureUrl,
                    successRedirectUrl = successUrl,
                    failureRedirectUrl = failureUrl,
                };
                try
                {
                    var resp = await http.PostAsJsonAsync($"{baseUrl}/payment/whish", body, ct);
                    var raw = await resp.Content.ReadAsStringAsync(ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger.LogError("Whish collect failed ({Code}) for {Ext}: {Body}", (int)resp.StatusCode, externalId, raw);
                        return new WhishCollectResult(false, null, $"Whish error {(int)resp.StatusCode}", raw);
                    }
                    using var jd = JsonDocument.Parse(raw);
                    var root = jd.RootElement;
                    var ok = !root.TryGetProperty("status", out var st) || st.ValueKind != JsonValueKind.False;
                    if (!ok)
                    {
                        var msg = root.TryGetProperty("dialog", out var dlg) && dlg.TryGetProperty("message", out var m)
                            ? m.GetString() : "Whish rejected the request.";
                        _logger.LogError("Whish collect rejected for {Ext}: {Body}", externalId, raw);
                        return new WhishCollectResult(false, null, msg, raw);
                    }
                    string? collectUrl = null;
                    if (root.TryGetProperty("data", out var data))
                    {
                        if (data.TryGetProperty("collectUrl", out var cu)) collectUrl = cu.GetString();
                        else if (data.TryGetProperty("url", out var u)) collectUrl = u.GetString();
                    }
                    if (string.IsNullOrWhiteSpace(collectUrl))
                    {
                        _logger.LogError("Whish collect returned no URL for {Ext}. Raw: {Body}", externalId, raw);
                        return new WhishCollectResult(false, null, "Whish did not return a payment URL.", raw);
                    }
                    return new WhishCollectResult(true, collectUrl, null, raw);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Whish collect threw for {Ext}", externalId);
                    return new WhishCollectResult(false, null, ex.Message);
                }
            }
        }

        public async Task<WhishCollectStatus> GetStatusAsync(string externalId, CancellationToken ct = default)
        {
            var (http, baseUrl) = await ClientAsync(ct);
            if (http is null) return new WhishCollectStatus(null, null);
            using (http)
            {
                try
                {
                    var resp = await http.PostAsJsonAsync($"{baseUrl}/payment/collect/status", new { externalId }, ct);
                    var raw = await resp.Content.ReadAsStringAsync(ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        _logger.LogWarning("Whish status check failed ({Code}) for {Ext}: {Body}", (int)resp.StatusCode, externalId, raw);
                        return new WhishCollectStatus(null, raw);
                    }
                    using var jd = JsonDocument.Parse(raw);
                    if (jd.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("collectStatus", out var cs))
                        return new WhishCollectStatus(cs.GetString()?.ToLowerInvariant(), raw);
                    _logger.LogWarning("Whish status check: unexpected shape for {Ext}: {Body}", externalId, raw);
                    return new WhishCollectStatus(null, raw);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Whish status check threw for {Ext}", externalId);
                    return new WhishCollectStatus(null, null);
                }
            }
        }
    }
}
