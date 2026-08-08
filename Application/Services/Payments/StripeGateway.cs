using System.Net.Http.Headers;
using System.Text.Json;
using Application.IServices;
using Microsoft.Extensions.Logging;

namespace Application.Services.Payments
{
    /// <summary>
    /// Stripe Checkout (hosted page) for card payments.
    ///
    /// Deliberately talks to Stripe over raw HTTP with form-encoded bodies
    /// rather than pulling in the Stripe.net SDK — one less dependency to
    /// keep in sync, and Checkout Sessions only need two fields.
    ///
    /// Flow:
    ///   1. StartAsync → POST /v1/checkout/sessions → { id, url }
    ///   2. Browser goes to `url`, pays on Stripe's page
    ///   3. Stripe POSTs checkout.session.completed to our webhook
    ///   4. Webhook matches session id → marks the registration Paid
    ///
    /// The session id is stored as ProviderRef so step 4 can find the row.
    /// </summary>
    public class StripeGateway : IPaymentGateway
    {
        public string Method => "Visa";

        private readonly IIntegrationSettingsService _settings;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<StripeGateway> _logger;

        public StripeGateway(
            IIntegrationSettingsService settings,
            IHttpClientFactory httpFactory,
            ILogger<StripeGateway> logger)
        {
            _settings = settings;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        public async Task<bool> IsConfiguredAsync(CancellationToken ct = default)
            => !string.IsNullOrWhiteSpace(await _settings.GetRawAsync("Stripe.SecretKey", ct));

        public async Task<PaymentStartResult> StartAsync(
            int registrationId, decimal amount, string currency, string description,
            string customerName, string? customerEmail,
            string successUrl, string cancelUrl, CancellationToken ct = default)
        {
            var key = await _settings.GetRawAsync("Stripe.SecretKey", ct);
            if (string.IsNullOrWhiteSpace(key))
                return new PaymentStartResult(false, null, null, "Stripe is not configured.");

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);

            // Stripe takes the smallest currency unit (cents for USD).
            var unitAmount = (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

            var form = new List<KeyValuePair<string, string>>
            {
                new("mode", "payment"),
                new("success_url", successUrl),
                new("cancel_url", cancelUrl),
                new("line_items[0][quantity]", "1"),
                new("line_items[0][price_data][currency]", currency.ToLowerInvariant()),
                new("line_items[0][price_data][unit_amount]", unitAmount.ToString()),
                new("line_items[0][price_data][product_data][name]", description),
                // Echoed back on the webhook so we can cross-check.
                new("metadata[registrationId]", registrationId.ToString()),
                new("metadata[customerName]", customerName),
            };
            if (!string.IsNullOrWhiteSpace(customerEmail))
                form.Add(new("customer_email", customerEmail));

            try
            {
                var resp = await http.PostAsync(
                    "https://api.stripe.com/v1/checkout/sessions",
                    new FormUrlEncodedContent(form), ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("Stripe session create failed ({Code}): {Body}", (int)resp.StatusCode, raw);
                    return new PaymentStartResult(false, null, null, $"Stripe error {(int)resp.StatusCode}");
                }

                using var jd = JsonDocument.Parse(raw);
                var sessionId = jd.RootElement.GetProperty("id").GetString();
                var url = jd.RootElement.GetProperty("url").GetString();
                return new PaymentStartResult(true, url, sessionId, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Stripe session create threw for registration {Id}", registrationId);
                return new PaymentStartResult(false, null, null, ex.Message);
            }
        }
    }
}
