using System.Net.Http.Headers;
using System.Text.Json;
using Application.IServices;
using Domain.Entities;

namespace Application.Services.Phase4
{
    public record TerminalProviderResult(string Status, string? ApprovalCode = null, string? Last4 = null, string? Scheme = null, string? Rrn = null, string? Message = null, string? Raw = null);

    /// <summary>One way of talking to a card terminal. Status is one of TerminalPaymentStatuses.</summary>
    public interface ICardTerminalProvider
    {
        string Name { get; }
        Task<TerminalProviderResult> StartAsync(CardTerminal terminal, TerminalPayment payment, CancellationToken ct);
        Task<TerminalProviderResult> GetStatusAsync(CardTerminal terminal, TerminalPayment payment, CancellationToken ct);
        Task CancelAsync(CardTerminal terminal, TerminalPayment payment, CancellationToken ct);
    }

    /// <summary>Stand-alone terminal: Axis waits for the cashier to type the approval code.</summary>
    public class ManualTerminalProvider : ICardTerminalProvider
    {
        public string Name => CardTerminalProviders.Manual;
        public Task<TerminalProviderResult> StartAsync(CardTerminal t, TerminalPayment p, CancellationToken ct)
            => Task.FromResult(new TerminalProviderResult(TerminalPaymentStatuses.Pending, Message: "Key the amount on the terminal, then enter the approval code."));
        public Task<TerminalProviderResult> GetStatusAsync(CardTerminal t, TerminalPayment p, CancellationToken ct)
            => Task.FromResult(new TerminalProviderResult(TerminalPaymentStatuses.Pending));
        public Task CancelAsync(CardTerminal t, TerminalPayment p, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Test terminal. Approves 3 seconds after the start; an amount ending in .13 is declined.
    /// Refuses to run unless IntegrationSettings CardTerminal.AllowSimulator = true.
    /// </summary>
    public class SimulatorTerminalProvider : ICardTerminalProvider
    {
        public const string AllowKey = "CardTerminal.AllowSimulator";
        public static readonly TimeSpan ApproveAfter = TimeSpan.FromSeconds(3);
        private readonly IIntegrationSettingsService _settings;
        public SimulatorTerminalProvider(IIntegrationSettingsService settings) { _settings = settings; }
        public string Name => CardTerminalProviders.Simulator;

        public async Task<TerminalProviderResult> StartAsync(CardTerminal t, TerminalPayment p, CancellationToken ct)
        {
            var allow = await _settings.GetRawAsync(AllowKey, ct);
            return string.Equals(allow?.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                ? new TerminalProviderResult(TerminalPaymentStatuses.Pending, Message: "Simulator: approving in 3 seconds.")
                : new TerminalProviderResult(TerminalPaymentStatuses.Failed, Message: $"The simulator is disabled. Set {AllowKey} = true only on test systems.");
        }

        public Task<TerminalProviderResult> GetStatusAsync(CardTerminal t, TerminalPayment p, CancellationToken ct)
        {
            if (DateTime.UtcNow - p.CreatedOn < ApproveAfter) return Task.FromResult(new TerminalProviderResult(TerminalPaymentStatuses.Pending));
            var cents = (int)Math.Round((p.Amount - Math.Floor(p.Amount)) * 100m);
            if (cents == 13) return Task.FromResult(new TerminalProviderResult(TerminalPaymentStatuses.Declined, Message: "Simulator: declined (amount ends in .13)."));
            var code = (Math.Abs(p.ExternalId.GetHashCode()) % 900000 + 100000).ToString();
            return Task.FromResult(new TerminalProviderResult(TerminalPaymentStatuses.Approved, code, "4242", "VISA", $"SIM{p.Id:D9}", "Simulator: approved."));
        }

        public Task CancelAsync(CardTerminal t, TerminalPayment p, CancellationToken ct) => Task.CompletedTask;
    }

    /// <summary>
    /// Generic HTTP contract, implemented by a bank / acquirer cloud API adapter or by a
    /// small bridge next to the terminal:
    ///   POST {BaseUrl}/payments                  {reference, terminalId, amount, currency, description}
    ///   GET  {BaseUrl}/payments/{reference}      → {status: pending|approved|declined|cancelled|failed, approvalCode, last4, scheme, rrn, message}
    ///   POST {BaseUrl}/payments/{reference}/cancel
    /// Settings: CardTerminal.Cloud.BaseUrl, CardTerminal.Cloud.ApiKey (bearer).
    /// The provider may also POST the final result to api/payments/terminal/callback.
    /// </summary>
    public class CloudHttpTerminalProvider : ICardTerminalProvider
    {
        public const string BaseUrlKey = "CardTerminal.Cloud.BaseUrl";
        public const string ApiKeyKey = "CardTerminal.Cloud.ApiKey";
        private readonly IIntegrationSettingsService _settings;
        private readonly IHttpClientFactory _http;
        public CloudHttpTerminalProvider(IIntegrationSettingsService settings, IHttpClientFactory http) { _settings = settings; _http = http; }
        public string Name => CardTerminalProviders.CloudHttp;

        private async Task<(HttpClient? client, string? baseUrl, string? error)> ClientAsync(CancellationToken ct)
        {
            var baseUrl = (await _settings.GetRawAsync(BaseUrlKey, ct))?.Trim().TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl)) return (null, null, $"Card terminal API is not configured. Set {BaseUrlKey} in Integrations.");
            var key = await _settings.GetRawAsync(ApiKeyKey, ct);
            var c = _http.CreateClient();
            c.Timeout = TimeSpan.FromSeconds(20);
            if (!string.IsNullOrWhiteSpace(key)) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
            return (c, baseUrl, null);
        }

        public async Task<TerminalProviderResult> StartAsync(CardTerminal t, TerminalPayment p, CancellationToken ct)
        {
            var (c, baseUrl, error) = await ClientAsync(ct);
            if (c is null) return new(TerminalPaymentStatuses.Failed, Message: error);
            using (c)
            {
                try
                {
                    // Fixed-length body (no chunked encoding): some bank gateways and bridges reject chunked requests.
                    var json = JsonSerializer.Serialize(new
                    {
                        reference = p.ExternalId, terminalId = t.ProviderTerminalId, amount = p.Amount, currency = p.Currency,
                        description = $"Axis invoice #{p.TransactionId}",
                    });
                    using var resp = await c.PostAsync($"{baseUrl}/payments", new StringContent(json, System.Text.Encoding.UTF8, "application/json"), ct);
                    var raw = await resp.Content.ReadAsStringAsync(ct);
                    if (!resp.IsSuccessStatusCode) return new(TerminalPaymentStatuses.Failed, Message: $"Terminal API refused the payment (HTTP {(int)resp.StatusCode}).", Raw: raw);
                    var parsed = Parse(raw);
                    // A start answer may already carry a final status; anything unknown means "sent, waiting".
                    return parsed.Status == TerminalPaymentStatuses.Pending
                        ? parsed with { Message = parsed.Message ?? "Sent to the terminal.", Raw = raw }
                        : parsed with { Raw = raw };
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    return new(TerminalPaymentStatuses.Failed, Message: "Terminal API unreachable: " + ex.Message);
                }
            }
        }

        public async Task<TerminalProviderResult> GetStatusAsync(CardTerminal t, TerminalPayment p, CancellationToken ct)
        {
            var (c, baseUrl, error) = await ClientAsync(ct);
            if (c is null) return new(TerminalPaymentStatuses.Pending, Message: error);
            using (c)
            {
                try
                {
                    using var resp = await c.GetAsync($"{baseUrl}/payments/{Uri.EscapeDataString(p.ExternalId)}", ct);
                    var raw = await resp.Content.ReadAsStringAsync(ct);
                    if (!resp.IsSuccessStatusCode) return new(TerminalPaymentStatuses.Pending, Message: $"Status check failed (HTTP {(int)resp.StatusCode}).");
                    return Parse(raw) with { Raw = raw };
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    return new(TerminalPaymentStatuses.Pending, Message: "Status check failed: " + ex.Message);
                }
            }
        }

        public async Task CancelAsync(CardTerminal t, TerminalPayment p, CancellationToken ct)
        {
            var (c, baseUrl, _) = await ClientAsync(ct);
            if (c is null) return;
            using (c)
            {
                try { using var _r = await c.PostAsync($"{baseUrl}/payments/{Uri.EscapeDataString(p.ExternalId)}/cancel", null, ct); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { /* best effort */ }
            }
        }

        public static TerminalProviderResult Parse(string raw)
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var r = doc.RootElement;
                string? S(string n) => r.ValueKind == JsonValueKind.Object && r.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                return new(MapStatus(S("status")), S("approvalCode"), Last4(S("last4")), S("scheme"), S("rrn"), S("message"));
            }
            catch (JsonException)
            {
                return new(TerminalPaymentStatuses.Pending, Message: "Unreadable answer from the terminal API.");
            }
        }

        public static string MapStatus(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
        {
            "approved" or "success" or "paid" or "captured" => TerminalPaymentStatuses.Approved,
            "declined" or "rejected" => TerminalPaymentStatuses.Declined,
            "cancelled" or "canceled" or "aborted" => TerminalPaymentStatuses.Cancelled,
            "failed" or "error" => TerminalPaymentStatuses.Failed,
            "pending" or "processing" or "waiting" or "sent" => TerminalPaymentStatuses.Pending,
            _ => TerminalPaymentStatuses.Pending,
        };

        public static string? Last4(string? s)
        {
            var digits = new string((s ?? "").Where(char.IsDigit).ToArray());
            return digits.Length >= 4 ? digits[^4..] : null;
        }
    }
}
