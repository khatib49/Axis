using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.IServices;
using Microsoft.Extensions.Logging;

namespace Application.Services.Shipping
{
    public interface IAramexClient
    {
        Task<string> GetEnvironmentAsync(CancellationToken ct = default);
        Task<AramexCreds?> LoadCredsAsync(string? environment = null, CancellationToken ct = default);
        Task<bool> IsConfiguredAsync(string? environment = null, CancellationToken ct = default);

        Task<AramexShipmentResult> CreateShipmentAsync(AramexShipmentRequest req, CancellationToken ct = default);
        Task<AramexShipmentResult> PrintLabelAsync(string awb, string productGroup, string originEntity, int reportId, CancellationToken ct = default);
        Task<AramexRateResult> CalculateRateAsync(AramexParty origin, AramexParty destination, decimal weightKg, int pieces,
            string productGroup, string productType, string paymentType, decimal? codAmount, string currency, CancellationToken ct = default);
        Task<AramexTrackingResult> TrackAsync(IEnumerable<string> awbs, CancellationToken ct = default);
        Task<AramexPickupResult> CreatePickupAsync(AramexPickupRequest req, CancellationToken ct = default);
        Task<(bool ok, string message)> CancelPickupAsync(string pickupGuid, CancellationToken ct = default);
        string TrackingUrl(string awb);
    }

    /// <summary>
    /// Aramex "Shipping Services API" (WCF, JSON flavour).
    ///
    ///   test : https://ws.dev.aramex.net/shippingapi.v2/{shipping|tracking|ratecalculator}/service_1_0.svc/json/{Method}
    ///   live : https://ws.aramex.net/shippingapi.v2/...
    ///
    /// Every call carries ClientInfo (username / password / account number /
    /// PIN / entity). Responses always have HasErrors + Notifications[{Code,
    /// Message}]; a CreateShipments response additionally has per-shipment
    /// HasErrors. Dates are WCF "/Date(ms)/" strings.
    ///
    /// Sandbox vs production is one switch (Aramex.Environment); each
    /// environment keeps its own credentials so the owner can flip without
    /// retyping anything — same pattern as MontyPay.
    /// </summary>
    public class AramexClient : IAramexClient
    {
        public const string SandboxBase = "https://ws.dev.aramex.net/shippingapi.v2";
        public const string ProductionBase = "https://ws.aramex.net/shippingapi.v2";

        private readonly IIntegrationSettingsService _settings;
        private readonly IHttpClientFactory _httpFactory;
        private readonly ILogger<AramexClient> _logger;

        internal static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = null,                      // Aramex wants PascalCase
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };

        public AramexClient(IIntegrationSettingsService settings, IHttpClientFactory httpFactory, ILogger<AramexClient> logger)
        {
            _settings = settings; _httpFactory = httpFactory; _logger = logger;
        }

        // ── Settings ─────────────────────────────────────────────────────
        public async Task<string> GetEnvironmentAsync(CancellationToken ct = default)
        {
            var env = (await _settings.GetRawAsync("Aramex.Environment", ct) ?? "sandbox").Trim().ToLowerInvariant();
            return env == "production" ? "production" : "sandbox";
        }

        public async Task<AramexCreds?> LoadCredsAsync(string? environment = null, CancellationToken ct = default)
        {
            var env = environment?.ToLowerInvariant() is "production" or "sandbox" ? environment!.ToLowerInvariant() : await GetEnvironmentAsync(ct);
            var p = env == "production" ? "Aramex.Production." : "Aramex.Sandbox.";

            var user = (await _settings.GetRawAsync(p + "UserName", ct))?.Trim();
            var pwd = await _settings.GetRawAsync(p + "Password", ct);
            var acc = (await _settings.GetRawAsync(p + "AccountNumber", ct))?.Trim();
            var pin = (await _settings.GetRawAsync(p + "AccountPin", ct))?.Trim();
            var entity = (await _settings.GetRawAsync(p + "AccountEntity", ct))?.Trim();
            var country = (await _settings.GetRawAsync("Aramex.AccountCountryCode", ct))?.Trim().ToUpperInvariant();
            var srcRaw = await _settings.GetRawAsync("Aramex.Source", ct);

            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(pwd) || string.IsNullOrWhiteSpace(acc)
                || string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(entity))
                return null;

            return new AramexCreds(env, env == "production" ? ProductionBase : SandboxBase,
                user, pwd!, acc, pin, entity, string.IsNullOrWhiteSpace(country) ? "LB" : country,
                int.TryParse(srcRaw, out var src) ? src : 24);
        }

        public async Task<bool> IsConfiguredAsync(string? environment = null, CancellationToken ct = default)
            => await LoadCredsAsync(environment, ct) is not null;

        public string TrackingUrl(string awb) => $"https://www.aramex.com/track/results?ShipmentNumber={Uri.EscapeDataString(awb)}";

        // ── HTTP ─────────────────────────────────────────────────────────
        private static readonly string[] SecretKeys = { "Password", "AccountPin" };

        /// <summary>Request JSON with the secrets blanked — what we store for support.</summary>
        internal static string Redact(string json)
        {
            foreach (var k in SecretKeys)
            {
                var needle = $"\"{k}\":\"";
                var i = 0;
                while ((i = json.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
                {
                    var start = i + needle.Length;
                    var end = json.IndexOf('"', start);
                    if (end < 0) break;
                    json = json[..start] + "***" + json[end..];
                    i = start + 3;
                }
            }
            return json;
        }

        private async Task<(JsonDocument? doc, string raw, string sentRedacted)> PostAsync(string service, string method, object body, CancellationToken ct)
        {
            var creds = await LoadCredsAsync(null, ct) ?? throw new InvalidOperationException("Aramex is not configured — fill the credentials under Admin → Integrations.");
            var url = $"{creds.BaseUrl}/{service}/service_1_0.svc/json/{method}";
            var json = JsonSerializer.Serialize(body, Json);
            using var http = _httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(40);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            HttpResponseMessage resp;
            try
            {
                resp = await http.PostAsync(url, content, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Aramex {Method} call failed ({Url})", method, url);
                return (null, $"HTTP error: {ex.Message}", Redact(json));
            }
            var raw = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Aramex {Method} HTTP {Code}: {Body}", method, (int)resp.StatusCode, raw.Length > 500 ? raw[..500] : raw);
                return (null, $"HTTP {(int)resp.StatusCode}: {raw}", Redact(json));
            }
            try { return (JsonDocument.Parse(raw), raw, Redact(json)); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Aramex {Method} returned non-JSON", method);
                return (null, raw, Redact(json));
            }
        }

        private static List<AramexNotification> Notifications(JsonElement el)
        {
            var list = new List<AramexNotification>();
            if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("Notifications", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var n in arr.EnumerateArray())
                    list.Add(new AramexNotification(Str(n, "Code") ?? "", Str(n, "Message") ?? ""));
            return list;
        }

        private static string? Str(JsonElement el, string name) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool Bool(JsonElement el, string name) =>
            el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

        private static object Transaction(string reference) => new
        {
            Reference1 = reference, Reference2 = "", Reference3 = "", Reference4 = "", Reference5 = "",
        };

        private static object Money(decimal value, string currency) => new { CurrencyCode = currency, Value = Math.Round(value, 2) };
        private static object Weight(decimal kg) => new { Unit = "KG", Value = Math.Round(Math.Max(0.01m, kg), 3) };

        // ── CreateShipments ──────────────────────────────────────────────
        public async Task<AramexShipmentResult> CreateShipmentAsync(AramexShipmentRequest req, CancellationToken ct = default)
        {
            var creds = await LoadCredsAsync(null, ct);
            if (creds is null) return new(false, null, null, null, new() { new("CONFIG", "Aramex credentials are not configured.") }, "", "");

            var cod = req.CodAmount is > 0;
            var body = new
            {
                ClientInfo = creds.ToClientInfo(),
                Transaction = Transaction(req.Reference),
                Shipments = new[]
                {
                    new
                    {
                        Reference1 = req.Reference, Reference2 = "", Reference3 = "",
                        ForeignHAWB = req.ForeignHawb,
                        TransportType = 0,
                        Shipper = req.Shipper.ToJson(),
                        Consignee = req.Consignee.ToJson(),
                        ThirdParty = (object?)null,
                        ShippingDateTime = WcfDate.Format(DateTime.UtcNow),
                        DueDate = WcfDate.Format(DateTime.UtcNow.AddDays(3)),
                        Comments = req.Comments ?? "",
                        PickupLocation = "Reception",
                        OperationsInstructions = "",
                        AccountsInstructions = "",
                        Details = new
                        {
                            Dimensions = (object?)null,
                            ActualWeight = Weight(req.WeightKg),
                            ChargeableWeight = (object?)null,
                            DescriptionOfGoods = req.DescriptionOfGoods,
                            GoodsOriginCountry = req.GoodsOriginCountry,
                            NumberOfPieces = Math.Max(1, req.Pieces),
                            ProductGroup = req.ProductGroup,
                            ProductType = req.ProductType,
                            PaymentType = req.PaymentType,
                            PaymentOptions = "",
                            CustomsValueAmount = (object?)null,
                            CashOnDeliveryAmount = cod ? Money(req.CodAmount!.Value, req.CodCurrency) : null,
                            InsuranceAmount = (object?)null,
                            CashAdditionalAmount = (object?)null,
                            CashAdditionalAmountDescription = "",
                            CollectAmount = (object?)null,
                            Services = cod ? "CODS" : "",
                            Items = Array.Empty<object>(),
                        },
                        Attachments = Array.Empty<object>(),
                        PickupGUID = req.PickupGuid ?? "",
                        Number = (string?)null,
                    },
                },
                LabelInfo = new { ReportID = req.LabelReportId, ReportType = "URL" },
            };

            var (doc, raw, sent) = await PostAsync("shipping", "CreateShipments", body, ct);
            if (doc is null) return new(false, null, null, null, new() { new("HTTP", raw) }, sent, raw);
            using (doc)
            {
                var root = doc.RootElement;
                var notes = Notifications(root);
                string? awb = null, label = null; byte[]? bytes = null;
                var shipOk = false;
                if (root.TryGetProperty("Shipments", out var ships) && ships.ValueKind == JsonValueKind.Array)
                {
                    foreach (var s in ships.EnumerateArray())
                    {
                        notes.AddRange(Notifications(s));
                        if (Bool(s, "HasErrors")) continue;
                        awb = Str(s, "ID");
                        if (s.TryGetProperty("ShipmentLabel", out var lbl) && lbl.ValueKind == JsonValueKind.Object)
                        {
                            label = Str(lbl, "LabelURL");
                            if (lbl.TryGetProperty("LabelFileContents", out var lf) && lf.ValueKind == JsonValueKind.String)
                                try { bytes = lf.GetBytesFromBase64(); } catch { /* ignore */ }
                        }
                        shipOk = !string.IsNullOrWhiteSpace(awb);
                    }
                }
                var ok = !Bool(root, "HasErrors") && shipOk;
                if (!ok && notes.Count == 0) notes.Add(new("ERR", "Aramex returned no shipment number."));
                return new(ok, awb, label, bytes, notes, sent, raw);
            }
        }

        // ── PrintLabel ───────────────────────────────────────────────────
        public async Task<AramexShipmentResult> PrintLabelAsync(string awb, string productGroup, string originEntity, int reportId, CancellationToken ct = default)
        {
            var creds = await LoadCredsAsync(null, ct);
            if (creds is null) return new(false, awb, null, null, new() { new("CONFIG", "Aramex credentials are not configured.") }, "", "");
            var body = new
            {
                ClientInfo = creds.ToClientInfo(),
                Transaction = Transaction(awb),
                ShipmentNumber = awb,
                ProductGroup = productGroup,
                OriginEntity = originEntity,
                LabelInfo = new { ReportID = reportId, ReportType = "URL" },
            };
            var (doc, raw, sent) = await PostAsync("shipping", "PrintLabel", body, ct);
            if (doc is null) return new(false, awb, null, null, new() { new("HTTP", raw) }, sent, raw);
            using (doc)
            {
                var root = doc.RootElement;
                var notes = Notifications(root);
                string? label = null;
                if (root.TryGetProperty("ShipmentLabel", out var lbl) && lbl.ValueKind == JsonValueKind.Object) label = Str(lbl, "LabelURL");
                return new(!Bool(root, "HasErrors") && label != null, awb, label, null, notes, sent, raw);
            }
        }

        // ── CalculateRate ────────────────────────────────────────────────
        public async Task<AramexRateResult> CalculateRateAsync(AramexParty origin, AramexParty destination, decimal weightKg, int pieces,
            string productGroup, string productType, string paymentType, decimal? codAmount, string currency, CancellationToken ct = default)
        {
            var creds = await LoadCredsAsync(null, ct);
            if (creds is null) return new(false, 0, currency, new() { new("CONFIG", "Aramex credentials are not configured.") }, "");
            var body = new
            {
                ClientInfo = creds.ToClientInfo(),
                Transaction = Transaction("rate"),
                OriginAddress = origin.AddressJson(),
                DestinationAddress = destination.AddressJson(),
                ShipmentDetails = new
                {
                    Dimensions = (object?)null,
                    ActualWeight = Weight(weightKg),
                    ChargeableWeight = (object?)null,
                    DescriptionOfGoods = "",
                    GoodsOriginCountry = origin.CountryCode,
                    NumberOfPieces = Math.Max(1, pieces),
                    ProductGroup = productGroup,
                    ProductType = productType,
                    PaymentType = paymentType,
                    PaymentOptions = "",
                    CustomsValueAmount = (object?)null,
                    CashOnDeliveryAmount = codAmount is > 0 ? Money(codAmount.Value, currency) : null,
                    InsuranceAmount = (object?)null,
                    CashAdditionalAmount = (object?)null,
                    CashAdditionalAmountDescription = "",
                    CollectAmount = (object?)null,
                    Services = codAmount is > 0 ? "CODS" : "",
                    Items = Array.Empty<object>(),
                },
                PreferredCurrencyCode = currency,
            };
            var (doc, raw, _) = await PostAsync("ratecalculator", "CalculateRate", body, ct);
            if (doc is null) return new(false, 0, currency, new() { new("HTTP", raw) }, raw);
            using (doc)
            {
                var root = doc.RootElement;
                var notes = Notifications(root);
                decimal amount = 0; var cur = currency;
                if (root.TryGetProperty("TotalAmount", out var ta) && ta.ValueKind == JsonValueKind.Object)
                {
                    if (ta.TryGetProperty("Value", out var v) && v.ValueKind == JsonValueKind.Number) amount = v.GetDecimal();
                    cur = Str(ta, "CurrencyCode") ?? currency;
                }
                var ok = !Bool(root, "HasErrors") && amount > 0;
                return new(ok, amount, cur, notes, raw);
            }
        }

        // ── TrackShipments ───────────────────────────────────────────────
        public async Task<AramexTrackingResult> TrackAsync(IEnumerable<string> awbs, CancellationToken ct = default)
        {
            var list = awbs.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct().ToList();
            if (list.Count == 0) return new(true, new(), new(), "");
            var creds = await LoadCredsAsync(null, ct);
            if (creds is null) return new(false, new(), new() { new("CONFIG", "Aramex credentials are not configured.") }, "");
            var body = new
            {
                ClientInfo = creds.ToClientInfo(),
                Transaction = Transaction("track"),
                Shipments = list,
                GetLastTrackingUpdateOnly = false,
            };
            var (doc, raw, _) = await PostAsync("tracking", "TrackShipments", body, ct);
            if (doc is null) return new(false, new(), new() { new("HTTP", raw) }, raw);
            using (doc)
            {
                var root = doc.RootElement;
                var notes = Notifications(root);
                var byAwb = new Dictionary<string, List<AramexTrackingUpdate>>(StringComparer.OrdinalIgnoreCase);
                if (root.TryGetProperty("TrackingResults", out var results) && results.ValueKind == JsonValueKind.Array)
                {
                    foreach (var kv in results.EnumerateArray())
                    {
                        var key = Str(kv, "Key") ?? "";
                        if (!kv.TryGetProperty("Value", out var updates) || updates.ValueKind != JsonValueKind.Array) continue;
                        var ups = new List<AramexTrackingUpdate>();
                        foreach (var u in updates.EnumerateArray())
                        {
                            var awb = Str(u, "WaybillNumber") ?? key;
                            ups.Add(new AramexTrackingUpdate(
                                awb,
                                Str(u, "UpdateCode") ?? "",
                                Str(u, "UpdateDescription") ?? "",
                                WcfDate.Parse(Str(u, "UpdateDateTime")) ?? DateTime.UtcNow,
                                Str(u, "UpdateLocation"),
                                Str(u, "Comments"),
                                Str(u, "ProblemCode")));
                        }
                        byAwb[key] = ups.OrderBy(x => x.At).ToList();
                    }
                }
                return new(!Bool(root, "HasErrors"), byAwb, notes, raw);
            }
        }

        // ── Pickup ───────────────────────────────────────────────────────
        public async Task<AramexPickupResult> CreatePickupAsync(AramexPickupRequest req, CancellationToken ct = default)
        {
            var creds = await LoadCredsAsync(null, ct);
            if (creds is null) return new(false, null, null, new() { new("CONFIG", "Aramex credentials are not configured.") }, "", "");
            var body = new
            {
                ClientInfo = creds.ToClientInfo(),
                Transaction = Transaction(req.Reference),
                Pickup = new
                {
                    PickupAddress = req.PickupParty.AddressJson(),
                    PickupContact = req.PickupParty.ContactJson(),
                    PickupLocation = req.PickupLocation,
                    PickupDate = WcfDate.Format(req.PickupDate),
                    ReadyTime = WcfDate.Format(req.ReadyTime),
                    LastPickupTime = WcfDate.Format(req.LastPickupTime),
                    ClosingTime = WcfDate.Format(req.ClosingTime),
                    Comments = req.Comments ?? "",
                    Reference1 = req.Reference, Reference2 = "",
                    Vehicle = "",
                    Shipments = Array.Empty<object>(),
                    PickupItems = new[]
                    {
                        new
                        {
                            ProductGroup = req.ProductGroup, ProductType = req.ProductType,
                            NumberOfShipments = Math.Max(1, req.NumberOfShipments),
                            PackageType = "Box",
                            Payment = req.PaymentType,
                            ShipmentWeight = Weight(req.TotalWeightKg),
                            ShipmentVolume = (object?)null,
                            NumberOfPieces = Math.Max(1, req.NumberOfPieces),
                            CashAmount = (object?)null,
                            ExtraCharges = (object?)null,
                            ShipmentDimensions = new { Length = 0, Width = 0, Height = 0, Unit = "" },
                            Comments = "",
                        },
                    },
                    Status = "Ready",
                    ExistingShipments = (object?)null,
                    Branch = "", RouteCode = "",
                },
                LabelInfo = (object?)null,
            };
            var (doc, raw, sent) = await PostAsync("shipping", "CreatePickup", body, ct);
            if (doc is null) return new(false, null, null, new() { new("HTTP", raw) }, sent, raw);
            using (doc)
            {
                var root = doc.RootElement;
                var notes = Notifications(root);
                string? id = null, guid = null;
                if (root.TryGetProperty("ProcessedPickup", out var pp) && pp.ValueKind == JsonValueKind.Object)
                {
                    id = Str(pp, "ID"); guid = Str(pp, "GUID");
                }
                return new(!Bool(root, "HasErrors") && id != null, id, guid, notes, sent, raw);
            }
        }

        public async Task<(bool ok, string message)> CancelPickupAsync(string pickupGuid, CancellationToken ct = default)
        {
            var creds = await LoadCredsAsync(null, ct);
            if (creds is null) return (false, "Aramex credentials are not configured.");
            var body = new { ClientInfo = creds.ToClientInfo(), Transaction = Transaction("cancel-pickup"), PickupGUID = pickupGuid, Comments = "Cancelled from AXIS" };
            var (doc, raw, _) = await PostAsync("shipping", "CancelPickup", body, ct);
            if (doc is null) return (false, raw);
            using (doc)
            {
                var root = doc.RootElement;
                var notes = Notifications(root);
                var ok = !Bool(root, "HasErrors");
                return (ok, ok ? "Pickup cancelled." : string.Join(" · ", notes.Select(n => n.ToString())));
            }
        }
    }
}
