using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.IServices;
using Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Application.Services.Payments
{
    /// <summary>
    /// MontyPay hosted Checkout (Akurateco platform) — Lebanon card acquiring.
    ///
    /// Flow (docs.montypay.com → Checkout):
    ///   1. POST {CHECKOUT_URL}/api/v1/session  {merchant_key, operation:"purchase",
    ///      order{number,amount,currency,description}, customer{...}, success_url,
    ///      cancel_url, notification_url?, hash}  → { redirect_url }
    ///   2. Customer pays on MontyPay's page, is sent back to success/cancel URL.
    ///   3. MontyPay POSTs a form-urlencoded callback to the notification URL on
    ///      every state change. The callback is the source of truth: a payment
    ///      is final only when type ∈ {sale,capture,…} AND status=success AND
    ///      order_status=settled.
    ///
    /// Hashes (default MD5 mode):  sha1(md5(UPPER(concat + password)))
    ///   session : order.number + amount + currency + description + password
    ///   callback: id + order_number + order_amount + order_currency + order_description + password
    ///   status  : payment_id + password   (or order_id + password)
    /// With "Use SHA256" enabled on the merchant: sha1(sha256(UPPER(...))).
    ///
    /// Sandbox vs production is a single switch (MontyPay.Environment); each
    /// environment keeps its own URL / merchant key / password so the owner
    /// can flip without re-typing anything.
    /// </summary>
    public class MontyPayProvider : IOnlinePaymentProvider
    {
        public const string ProviderKey = "MontyPay";
        public string Key => ProviderKey;

        private readonly IIntegrationSettingsService _settings;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<MontyPayProvider> _logger;

        public MontyPayProvider(IIntegrationSettingsService settings, IHttpClientFactory httpFactory, ILogger<MontyPayProvider> logger)
        {
            _settings = settings;
            _httpFactory = httpFactory;
            _logger = logger;
        }

        // ── Settings ─────────────────────────────────────────────────────
        private sealed record Creds(string Environment, string CheckoutUrl, string MerchantKey, string Password, bool UseSha256);

        public async Task<string> GetEnvironmentAsync(CancellationToken ct = default)
        {
            var env = (await _settings.GetRawAsync("MontyPay.Environment", ct) ?? "sandbox").Trim().ToLowerInvariant();
            return env == "production" ? "production" : "sandbox";
        }

        private async Task<Creds?> LoadAsync(string? environment, CancellationToken ct)
        {
            var env = environment?.ToLowerInvariant() is "production" or "sandbox"
                ? environment!.ToLowerInvariant()
                : await GetEnvironmentAsync(ct);
            var prefix = env == "production" ? "MontyPay.Production." : "MontyPay.Sandbox.";

            var url = (await _settings.GetRawAsync(prefix + "CheckoutUrl", ct))?.Trim().TrimEnd('/');
            var key = (await _settings.GetRawAsync(prefix + "MerchantKey", ct))?.Trim();
            var pwd = (await _settings.GetRawAsync(prefix + "Password", ct))?.Trim();
            var sha = string.Equals(await _settings.GetRawAsync("MontyPay.HashAlgorithm", ct), "sha256", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(pwd))
                return null;
            return new Creds(env, url, key, pwd, sha);
        }

        public async Task<bool> IsConfiguredAsync(string? environment = null, CancellationToken ct = default)
            => await LoadAsync(environment, ct) is not null;

        // ── Hashing ──────────────────────────────────────────────────────
        // The platform uppercases with PHP strtoupper (ASCII only) for
        // callbacks and mb_strtoupper for requests; our values are ASCII in
        // practice (amounts, USD, plain descriptions) so one helper serves both.
        private static string AsciiUpper(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s) sb.Append(c is >= 'a' and <= 'z' ? (char)(c - 32) : c);
            return sb.ToString();
        }

        private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

        internal static string ComputeHash(string concat, string password, bool useSha256)
        {
            var raw = AsciiUpper(concat + password);
            var inner = useSha256
                ? Hex(SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
                : Hex(MD5.HashData(Encoding.UTF8.GetBytes(raw)));
            return Hex(SHA1.HashData(Encoding.UTF8.GetBytes(inner)));
        }

        internal static string FormatAmount(decimal amount) =>
            amount.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>Description goes into the hash verbatim — keep it ASCII and stable.</summary>
        internal static string SafeDescription(string? d)
        {
            var s = (d ?? "Payment").Trim();
            var sb = new StringBuilder();
            foreach (var c in s) sb.Append(c < 128 ? c : '-');
            var res = sb.ToString();
            return res.Length > 120 ? res[..120] : (res.Length == 0 ? "Payment" : res);
        }

        // ── Session ──────────────────────────────────────────────────────
        public async Task<CheckoutSessionResult> CreateCheckoutAsync(
            OnlinePayment payment, string successUrl, string cancelUrl, string notificationUrl, CancellationToken ct = default)
        {
            var creds = await LoadAsync(payment.Environment, ct);
            if (creds is null)
                return new CheckoutSessionResult(false, null, null, $"MontyPay ({payment.Environment}) is not configured.");

            // order.number must be unique per attempt → code + attempt stamp.
            var orderNumber = $"AX-{payment.Code}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds() % 100000000}";
            var amount = FormatAmount(payment.Amount);
            var currency = (payment.Currency ?? "USD").ToUpperInvariant();
            var description = SafeDescription(payment.Description);
            var hash = ComputeHash(orderNumber + amount + currency + description, creds.Password, creds.UseSha256);

            var sendNotify = !string.Equals(await _settings.GetRawAsync("MontyPay.SendNotificationUrl", ct), "false", StringComparison.OrdinalIgnoreCase);

            var body = new Dictionary<string, object?>
            {
                ["merchant_key"] = creds.MerchantKey,
                ["operation"] = "purchase",
                ["order"] = new Dictionary<string, object?>
                {
                    ["number"] = orderNumber,
                    ["amount"] = amount,
                    ["currency"] = currency,
                    ["description"] = description,
                },
                ["success_url"] = successUrl,
                ["cancel_url"] = cancelUrl,
                ["hash"] = hash,
            };
            if (sendNotify && !string.IsNullOrWhiteSpace(notificationUrl))
                body["notification_url"] = notificationUrl;

            if (!string.IsNullOrWhiteSpace(payment.CustomerName) || !string.IsNullOrWhiteSpace(payment.CustomerEmail))
            {
                var customer = new Dictionary<string, object?>();
                if (!string.IsNullOrWhiteSpace(payment.CustomerName)) customer["name"] = payment.CustomerName!.Trim();
                if (!string.IsNullOrWhiteSpace(payment.CustomerEmail)) customer["email"] = payment.CustomerEmail!.Trim();
                body["customer"] = customer;
            }

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);

            try
            {
                var resp = await http.PostAsJsonAsync($"{creds.CheckoutUrl}/api/v1/session", body, ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogError("MontyPay session failed ({Code}) for payment {Id}: {Body}", (int)resp.StatusCode, payment.Id, raw);
                    var msg = TryReadError(raw) ?? $"MontyPay error {(int)resp.StatusCode}";
                    return new CheckoutSessionResult(false, null, orderNumber, msg, raw);
                }

                using var jd = JsonDocument.Parse(raw);
                var root = jd.RootElement;
                string? redirect = null;
                if (root.TryGetProperty("redirect_url", out var ru)) redirect = ru.GetString();
                else if (root.TryGetProperty("redirectUrl", out var ru2)) redirect = ru2.GetString();

                if (string.IsNullOrWhiteSpace(redirect))
                {
                    _logger.LogError("MontyPay session returned no redirect_url for payment {Id}: {Body}", payment.Id, raw);
                    return new CheckoutSessionResult(false, null, orderNumber, TryReadError(raw) ?? "MontyPay did not return a payment page URL.", raw);
                }

                return new CheckoutSessionResult(true, redirect, orderNumber, null, raw);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MontyPay session threw for payment {Id}", payment.Id);
                return new CheckoutSessionResult(false, null, orderNumber, ex.Message);
            }
        }

        private static string? TryReadError(string raw)
        {
            try
            {
                using var jd = JsonDocument.Parse(raw);
                var root = jd.RootElement;
                if (root.TryGetProperty("error_message", out var em)) return em.GetString();
                if (root.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
                {
                    var parts = new List<string>();
                    foreach (var e in errs.EnumerateArray())
                        if (e.TryGetProperty("error_message", out var m)) parts.Add(m.GetString() ?? "");
                    if (parts.Count > 0) return string.Join("; ", parts);
                }
                if (root.TryGetProperty("message", out var msg)) return msg.GetString();
            }
            catch { /* not JSON */ }
            return null;
        }

        // ── Callback ─────────────────────────────────────────────────────
        private static readonly HashSet<string> SettlingTypes = new(StringComparer.OrdinalIgnoreCase)
            { "sale", "capture", "recurring", "debit", "transfer", "credit" };

        public async Task<ProviderCallbackResult> ParseCallbackAsync(IReadOnlyDictionary<string, string> form, string raw, CancellationToken ct = default)
        {
            string? G(string k) => form.TryGetValue(k, out var v) ? v : null;

            var id = G("id");
            var orderNumber = G("order_number");
            var amountStr = G("order_amount");
            var currency = G("order_currency");
            var description = G("order_description");
            var orderStatus = G("order_status");
            var type = G("type");
            var status = G("status");
            var hash = G("hash");

            // Verify against BOTH environments' passwords — a sandbox callback
            // can still arrive after the switch was flipped to production.
            var hashValid = false;
            if (!string.IsNullOrWhiteSpace(hash) && id is not null && orderNumber is not null)
            {
                var concat = id + orderNumber + (amountStr ?? "") + (currency ?? "") + (description ?? "");
                foreach (var env in new[] { "sandbox", "production" })
                {
                    var c = await LoadAsync(env, ct);
                    if (c is null) continue;
                    if (string.Equals(ComputeHash(concat, c.Password, c.UseSha256), hash, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ComputeHash(concat, c.Password, !c.UseSha256), hash, StringComparison.OrdinalIgnoreCase))
                    { hashValid = true; break; }
                }
            }

            decimal? amount = decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var a) ? a : null;

            return new ProviderCallbackResult(
                HashValid: hashValid,
                OrderNumber: orderNumber,
                ProviderPaymentId: id,
                ProviderType: type,
                ProviderStatus: status,
                OrderStatus: orderStatus,
                Outcome: MapOutcome(type, status, orderStatus),
                Reason: G("reason"),
                PaymentMethod: G("checkout_payment_method") ?? G("payment_method") ?? G("digital_wallet") ?? G("brand"),
                CardMasked: G("card"),
                Amount: amount,
                Currency: currency,
                Raw: raw);
        }

        /// <summary>Decision table from the Callbacks guide — order_status decides finality.</summary>
        internal static string? MapOutcome(string? type, string? status, string? orderStatus)
        {
            var t = (type ?? "").ToLowerInvariant();
            var s = (status ?? "").ToLowerInvariant();
            var o = (orderStatus ?? "").ToLowerInvariant();

            if (t == "chargeback") return "Chargeback";
            if (t == "reversal") return "Voided";
            if (t == "refund" && s == "success") return "Refunded";
            if (t == "void" && s == "success") return "Voided";
            // A refund/void that FAILED leaves the payment exactly as it was.
            if ((t is "refund" or "void") && s == "fail") return null;

            if (s == "fail" || o == "decline") return "Failed";

            if (SettlingTypes.Contains(t) && s == "success" && o == "settled") return "Paid";
            if (o == "settled" && s == "success") return "Paid";          // status-check shape
            if (o == "refund") return "Refunded";
            if (o == "void") return "Voided";
            if (o == "chargeback") return "Chargeback";
            if (o == "decline") return "Failed";

            // 3ds / redirect / prepare / pending / waiting / undefined → keep waiting
            return null;
        }

        // ── Status check (reconcile) ─────────────────────────────────────
        public async Task<ProviderCallbackResult?> QueryStatusAsync(OnlinePayment payment, CancellationToken ct = default)
        {
            var creds = await LoadAsync(payment.Environment, ct);
            if (creds is null) return null;

            // Prefer the gateway payment id (known after the first callback);
            // fall back to our order number.
            Dictionary<string, object?> body;
            if (!string.IsNullOrWhiteSpace(payment.ProviderPaymentId))
                body = new()
                {
                    ["merchant_key"] = creds.MerchantKey,
                    ["payment_id"] = payment.ProviderPaymentId,
                    ["hash"] = ComputeHash(payment.ProviderPaymentId!, creds.Password, creds.UseSha256),
                };
            else if (!string.IsNullOrWhiteSpace(payment.ProviderOrderNumber))
                body = new()
                {
                    ["merchant_key"] = creds.MerchantKey,
                    ["order_id"] = payment.ProviderOrderNumber,
                    ["hash"] = ComputeHash(payment.ProviderOrderNumber!, creds.Password, creds.UseSha256),
                };
            else return null;

            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            try
            {
                var resp = await http.PostAsJsonAsync($"{creds.CheckoutUrl}/api/v1/payment/status", body, ct);
                var raw = await resp.Content.ReadAsStringAsync(ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("MontyPay status check failed ({Code}) for payment {Id}: {Body}", (int)resp.StatusCode, payment.Id, raw);
                    return new ProviderCallbackResult(true, payment.ProviderOrderNumber, payment.ProviderPaymentId, "status", "error", null, null,
                        TryReadError(raw) ?? $"HTTP {(int)resp.StatusCode}", null, null, null, null, raw);
                }

                using var jd = JsonDocument.Parse(raw);
                var root = jd.RootElement;
                string? S(string k) => root.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null
                    ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
                    : null;

                // Status responses carry order_status (+ optionally the last
                // event's type/status); some shapes only carry "status" with
                // the ORDER status in it. Never let an order status masquerade
                // as an event status.
                var hasOrderStatus = root.TryGetProperty("order_status", out _);
                var orderStatus = hasOrderStatus ? S("order_status") : S("status");
                var status = hasOrderStatus ? S("status") : null;
                var type = S("type") ?? "sale";
                var outcome = MapOutcome(type, status ?? (orderStatus == "settled" ? "success" : null), orderStatus);

                string? amountStr = S("order_amount") ?? S("amount");
                if (amountStr is null && root.TryGetProperty("order", out var orderObj) && orderObj.ValueKind == JsonValueKind.Object
                    && orderObj.TryGetProperty("amount", out var oa))
                    amountStr = oa.ValueKind == JsonValueKind.String ? oa.GetString() : oa.ToString();

                return new ProviderCallbackResult(
                    HashValid: true,
                    OrderNumber: S("order_number") ?? payment.ProviderOrderNumber,
                    ProviderPaymentId: S("id") ?? S("payment_id") ?? payment.ProviderPaymentId,
                    ProviderType: type, ProviderStatus: status, OrderStatus: orderStatus,
                    Outcome: outcome, Reason: S("reason") ?? S("decline_reason"),
                    PaymentMethod: S("payment_method") ?? S("brand"), CardMasked: S("card"),
                    Amount: decimal.TryParse(amountStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var amt) ? amt : null,
                    Currency: S("order_currency"), Raw: raw);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MontyPay status check threw for payment {Id}", payment.Id);
                return null;
            }
        }
    }
}
