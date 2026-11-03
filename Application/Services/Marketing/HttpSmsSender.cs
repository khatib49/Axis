using System.Net.Http.Headers;
using System.Net.Http.Json;
using Application.IServices;
using Microsoft.Extensions.Logging;

namespace Application.Services.Marketing
{
    /// <summary>
    /// Generic HTTP SMS gateway over IntegrationSettings: Sms.Provider ("http"),
    /// Sms.Url, Sms.ApiKey (bearer), Sms.From. POSTs JSON {to, from, text}.
    /// Missing settings → Configured = false, "SMS not configured". Never throws.
    /// </summary>
    public class HttpSmsSender : ISmsSender
    {
        private readonly IIntegrationSettingsService _settings;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<HttpSmsSender> _logger;

        public HttpSmsSender(IIntegrationSettingsService settings, IHttpClientFactory httpFactory, ILogger<HttpSmsSender> logger)
        {
            _settings = settings; _httpFactory = httpFactory; _logger = logger;
        }

        public async Task<SmsSendResult> SendAsync(string to, string text, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(to)) return new(false, true, "No recipient.");
            var provider = (await _settings.GetRawAsync("Sms.Provider", ct))?.Trim();
            var url = (await _settings.GetRawAsync("Sms.Url", ct))?.Trim();
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(url))
            {
                _logger.LogWarning("SMS not configured (Sms.Provider / Sms.Url missing); message to {To} not sent", to);
                return new(false, false, "SMS not configured");
            }
            if (!string.Equals(provider, "http", StringComparison.OrdinalIgnoreCase))
                return new(false, false, $"Unsupported Sms.Provider '{provider}' (only 'http').");
            var apiKey = await _settings.GetRawAsync("Sms.ApiKey", ct);
            var from = await _settings.GetRawAsync("Sms.From", ct);
            try
            {
                using var http = _httpFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(20);
                if (!string.IsNullOrWhiteSpace(apiKey))
                    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
                using var res = await http.PostAsJsonAsync(url, new { to = to.Trim(), from = from?.Trim(), text }, ct);
                if (res.IsSuccessStatusCode) return new(true, true, null);
                var body = await res.Content.ReadAsStringAsync(ct);
                var err = $"SMS gateway returned {(int)res.StatusCode}: {(body.Length > 200 ? body[..200] : body)}";
                _logger.LogWarning("SMS to {To} failed: {Error}", to, err);
                return new(false, true, err);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMS to {To} failed", to);
                return new(false, true, ex.Message);
            }
        }
    }
}
