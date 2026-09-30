using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Services.Shipping
{
    /// <summary>
    /// Delivery for website orders.
    ///
    /// Money
    ///   • COD order: Accept → OPEN invoice (no cash yet) → shipped → Aramex says
    ///     Delivered → we CLOSE the invoice; JournalService books the sale to
    ///     1060 "Aramex COD Receivable" (not 1000) and we book the delivery fee
    ///     DR 1060 / CR 4400. When Aramex remits, SettleCodAsync books
    ///     DR 1000|1050 (net) + DR 5400 (courier fees) / CR 1060 (COD total).
    ///   • Card-paid delivery order: sale goes to 1050 at payment time; fee
    ///     DR 1050 / CR 4400 at the same moment.
    ///
    /// Every Aramex failure is stored on the shipment (Error + raw JSON) and
    /// surfaced to the till — nothing is silently retried.
    /// </summary>
    public class ShippingService : IShippingService
    {
        private readonly IBaseRepository<ShippingZone> _zones;
        private readonly IBaseRepository<Shipment> _shipments;
        private readonly IBaseRepository<ShipmentEvent> _events;
        private readonly IBaseRepository<OnlineOrder> _orders;
        private readonly IBaseRepository<TransactionRecord> _tx;
        private readonly IBaseRepository<Account> _accounts;
        private readonly IAramexClient _aramex;
        private readonly IIntegrationSettingsService _settings;
        private readonly IJournalService _journal;
        private readonly IUnitOfWork _uow;
        private readonly IServiceProvider _sp;
        private readonly ILogger<ShippingService> _logger;

        public ShippingService(
            IBaseRepository<ShippingZone> zones, IBaseRepository<Shipment> shipments, IBaseRepository<ShipmentEvent> events,
            IBaseRepository<OnlineOrder> orders, IBaseRepository<TransactionRecord> tx, IBaseRepository<Account> accounts,
            IAramexClient aramex, IIntegrationSettingsService settings, IJournalService journal,
            IUnitOfWork uow, IServiceProvider sp, ILogger<ShippingService> logger)
        {
            _zones = zones; _shipments = shipments; _events = events; _orders = orders; _tx = tx; _accounts = accounts;
            _aramex = aramex; _settings = settings; _journal = journal; _uow = uow; _sp = sp; _logger = logger;
        }

        // ── Settings ─────────────────────────────────────────────────────
        private async Task<string> S(string key, string fallback, CancellationToken ct)
            => (await _settings.GetRawAsync(key, ct))?.Trim() is { Length: > 0 } v ? v : fallback;
        private async Task<string?> S(string key, CancellationToken ct)
            => (await _settings.GetRawAsync(key, ct))?.Trim() is { Length: > 0 } v ? v : null;
        private async Task<bool> Flag(string key, bool fallback, CancellationToken ct)
        {
            var raw = await _settings.GetRawAsync(key, ct);
            return raw is null ? fallback : raw.Trim().ToLowerInvariant() is "true" or "1" or "yes" or "on";
        }

        public Task<bool> IsDeliveryEnabledAsync(CancellationToken ct = default) => Flag("Shop.DeliveryEnabled", true, ct);
        public Task<bool> IsCodEnabledAsync(CancellationToken ct = default) => Flag("Shop.CodEnabled", true, ct);
        public async Task<decimal> DefaultWeightKgAsync(CancellationToken ct = default)
            => decimal.TryParse(await S("Shipping.DefaultWeightKg", "0.5", ct), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var w) && w > 0 ? w : 0.5m;

        private async Task<string> RateModeAsync(CancellationToken ct)
        {
            var m = (await S("Shop.RateMode", "zones", ct)).ToLowerInvariant();
            return m is "aramex" or "free" ? m : "zones";
        }

        private async Task<AramexParty?> ShipperPartyAsync(CancellationToken ct)
        {
            var creds = await _aramex.LoadCredsAsync(null, ct);
            var line1 = await S("Shipping.Shipper.Line1", ct);
            var city = await S("Shipping.Shipper.City", "Beirut", ct);
            var phone = await S("Shipping.Shipper.Phone", ct);
            if (line1 is null || line1.Length < 4 || phone is null) return null;
            return new AramexParty(
                creds?.AccountNumber, line1, await S("Shipping.Shipper.Line2", ct), city, await S("Shipping.Shipper.CountryCode", "LB", ct),
                await S("Shipping.Shipper.PersonName", "AXIS", ct), await S("Shipping.Shipper.CompanyName", "AXIS Game Lounge", ct),
                phone, await S("Shipping.Shipper.Cell", phone, ct), await S("Shipping.Shipper.Email", "info@axislb.com", ct));
        }

        public async Task<ShippingSettingsDto> GetSettingsAsync(CancellationToken ct = default)
        {
            var env = await _aramex.GetEnvironmentAsync(ct);
            return new ShippingSettingsDto(
                env,
                await _aramex.IsConfiguredAsync("sandbox", ct),
                await _aramex.IsConfiguredAsync("production", ct),
                await RateModeAsync(ct),
                await IsDeliveryEnabledAsync(ct), await IsCodEnabledAsync(ct), await Flag("Shop.Enabled", true, ct),
                await S("Aramex.ProductGroup", "DOM", ct), await S("Aramex.ProductType", "OND", ct), await S("Aramex.PaymentType", "P", ct), await S("Aramex.CodCurrency", "USD", ct),
                await S("Shipping.Shipper.CompanyName", ct), await S("Shipping.Shipper.PersonName", ct), await S("Shipping.Shipper.Phone", ct), await S("Shipping.Shipper.Cell", ct), await S("Shipping.Shipper.Email", ct),
                await S("Shipping.Shipper.Line1", ct), await S("Shipping.Shipper.Line2", ct), await S("Shipping.Shipper.City", ct), await S("Shipping.Shipper.CountryCode", "LB", ct),
                await DefaultWeightKgAsync(ct),
                int.TryParse(await S("Aramex.TrackingPollMinutes", "30", ct), out var mins) && mins >= 5 ? mins : 30,
                AramexClient.SandboxBase, AramexClient.ProductionBase);
        }

        public async Task<ShippingTestResult> TestConnectionAsync(CancellationToken ct = default)
        {
            if (!await _aramex.IsConfiguredAsync(null, ct))
                return new(false, "Aramex credentials are incomplete for the selected environment (username, password, account number, PIN, entity).");
            var shipper = await ShipperPartyAsync(ct);
            if (shipper is null)
                return new(false, "Fill the shipper profile first (Shipping.Shipper.Line1 and Phone).");
            var dest = shipper with { City = "Beirut", Line1 = "Test address, Hamra" };
            var rate = await _aramex.CalculateRateAsync(shipper, dest, 0.5m, 1,
                await S("Aramex.ProductGroup", "DOM", ct), await S("Aramex.ProductType", "OND", ct), await S("Aramex.PaymentType", "P", ct),
                null, await S("Aramex.CodCurrency", "USD", ct), ct);
            if (rate.Ok) return new(true, $"Aramex OK — a 0.5 kg parcel inside Beirut is quoted at {rate.Amount:0.00} {rate.Currency}.", rate.Amount, rate.Currency);

            // Bad credentials are unambiguous; anything else may just be the rate API not enabled on the account.
            var text = rate.ErrorText;
            if (text.Contains("ERR01", StringComparison.OrdinalIgnoreCase) || text.Contains("ERR02", StringComparison.OrdinalIgnoreCase) || text.Contains("ERR03", StringComparison.OrdinalIgnoreCase))
                return new(false, "Aramex rejected the credentials: " + text);
            return new(false, "Aramex answered but could not quote a rate (credentials may still be fine — ask Aramex to enable rate calculation): " + text);
        }

        // ── Zones ────────────────────────────────────────────────────────
        private static ShippingZoneDto ZoneDto(ShippingZone z) => new(z.Id, z.Name, z.Cities, z.Fee, z.FreeAbove, z.EstimatedDays, z.SortOrder, z.IsActive);

        public async Task<List<ShippingZoneDto>> ListZonesAsync(bool includeInactive, CancellationToken ct = default)
        {
            var q = _zones.Query();
            if (!includeInactive) q = q.Where(z => z.IsActive);
            return (await q.OrderBy(z => z.SortOrder).ThenBy(z => z.Name).ToListAsync(ct)).Select(ZoneDto).ToList();
        }

        public async Task<ShippingZoneDto> UpsertZoneAsync(int? id, ShippingZoneUpsertDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Zone name is required.");
            if (dto.Fee < 0) throw new ArgumentException("Fee cannot be negative.");
            ShippingZone z;
            if (id is > 0)
            {
                z = await _zones.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new KeyNotFoundException("Zone not found.");
            }
            else
            {
                z = new ShippingZone();
                await _zones.AddAsync(z, ct);
            }
            z.Name = dto.Name.Trim();
            z.Cities = string.Join(", ", (dto.Cities ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase));
            z.Fee = Math.Round(dto.Fee, 2);
            z.FreeAbove = dto.FreeAbove is > 0 ? Math.Round(dto.FreeAbove.Value, 2) : null;
            z.EstimatedDays = dto.EstimatedDays?.Trim();
            z.SortOrder = dto.SortOrder;
            z.IsActive = dto.IsActive;
            await _uow.SaveChangesAsync(ct);
            return ZoneDto(z);
        }

        public async Task<bool> DeleteZoneAsync(int id, CancellationToken ct = default)
        {
            var z = await _zones.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (z is null) return false;
            var used = await _orders.Query().AnyAsync(o => o.ShippingZoneId == id, ct);
            if (used) { z.IsActive = false; }          // keep history intact
            else _zones.Remove(z);
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        private static string Norm(string s) => (s ?? "").Trim().ToLowerInvariant();

        private async Task<ShippingZone?> MatchZoneAsync(string city, CancellationToken ct)
        {
            var c = Norm(city);
            if (c.Length == 0) return null;
            var zones = await _zones.Query().Where(z => z.IsActive).OrderBy(z => z.SortOrder).ToListAsync(ct);
            return zones.FirstOrDefault(z => z.CityList().Any(x => Norm(x) == c))
                ?? zones.FirstOrDefault(z => Norm(z.Name) == c)
                ?? zones.FirstOrDefault(z => z.CityList().Any(x => c.Contains(Norm(x)) || Norm(x).Contains(c)));
        }

        // ── Quote ────────────────────────────────────────────────────────
        public async Task<ShopQuoteDto> QuoteAsync(decimal subtotal, decimal weightKg, int pieces, string city, decimal? codAmount, CancellationToken ct = default)
        {
            if (!await IsDeliveryEnabledAsync(ct))
                return new(false, "Delivery is not available at the moment — choose pickup.", subtotal, 0, subtotal, weightKg, "none", null, null, null);
            if (string.IsNullOrWhiteSpace(city))
                return new(false, "Choose your city.", subtotal, 0, subtotal, weightKg, "none", null, null, null);

            var mode = await RateModeAsync(ct);
            var zone = await MatchZoneAsync(city, ct);

            if (mode == "free")
                return new(true, null, subtotal, 0, subtotal, weightKg, "free", zone?.Name, zone?.EstimatedDays, zone?.Id);

            decimal fee; string source;
            if (mode == "aramex")
            {
                var shipper = await ShipperPartyAsync(ct);
                if (shipper is not null && await _aramex.IsConfiguredAsync(null, ct))
                {
                    var dest = shipper with { City = city.Trim(), Line1 = "Customer address" };
                    var rate = await _aramex.CalculateRateAsync(shipper, dest, Math.Max(0.01m, weightKg), Math.Max(1, pieces),
                        await S("Aramex.ProductGroup", "DOM", ct), await S("Aramex.ProductType", "OND", ct), await S("Aramex.PaymentType", "P", ct),
                        codAmount, await S("Aramex.CodCurrency", "USD", ct), ct);
                    if (rate.Ok)
                    {
                        fee = Math.Ceiling(rate.Amount * 2) / 2m;      // round up to the nearest 0.50
                        source = "aramex";
                        if (zone?.FreeAbove is decimal fa && subtotal >= fa) { fee = 0; source = "free"; }
                        return new(true, null, subtotal, fee, subtotal + fee, weightKg, source, zone?.Name, zone?.EstimatedDays, zone?.Id);
                    }
                    _logger.LogWarning("Aramex rate failed for {City}: {Err} — falling back to zones", city, rate.ErrorText);
                }
            }

            if (zone is null)
                return new(false, $"We don't deliver to \"{city.Trim()}\" yet — choose pickup or another city.", subtotal, 0, subtotal, weightKg, "none", null, null, null);

            fee = zone.Fee; source = "zone";
            if (zone.FreeAbove is decimal free && subtotal >= free) { fee = 0; source = "free"; }
            return new(true, null, subtotal, fee, subtotal + fee, weightKg, source, zone.Name, zone.EstimatedDays, zone.Id);
        }

        // ── DTO ──────────────────────────────────────────────────────────
        private ShipmentDto ToDto(Shipment s, OnlineOrder? o = null)
        {
            o ??= s.Order;
            return new ShipmentDto(
                s.Id, s.OnlineOrderId, o?.Code ?? "", s.Provider, s.Environment, s.AwbNumber, s.Status,
                s.ProductGroup, s.ProductType, s.PaymentType, s.Services, s.CodAmount, s.CodCurrency, s.WeightKg, s.Pieces,
                s.LabelUrl, s.PickupGuid, s.PickupId,
                s.LastTrackingCode, s.LastTrackingText, s.LastTrackingAt, s.LastPolledAt,
                s.Error, s.CreatedBy, s.CreatedOn, s.DeliveredOn, s.SettledOn, s.SettlementJournalEntryId,
                s.AwbNumber is null ? null : _aramex.TrackingUrl(s.AwbNumber),
                (s.Events ?? new List<ShipmentEvent>()).OrderByDescending(e => e.EventAt).Select(e => new ShipmentEventDto(e.Id, e.Code, e.Description, e.Location, e.Comments, e.EventAt)).ToList(),
                o?.CustomerName, o?.CustomerPhone, o?.City, o?.PaymentMode, o?.Total ?? 0m, o?.Status);
        }

        public async Task<ShipmentDto?> GetShipmentAsync(int shipmentId, CancellationToken ct = default)
        {
            var s = await _shipments.Query().Include(x => x.Events).Include(x => x.Order).FirstOrDefaultAsync(x => x.Id == shipmentId, ct);
            return s is null ? null : ToDto(s);
        }

        public async Task<ShipmentDto?> GetShipmentForOrderAsync(int orderId, CancellationToken ct = default)
        {
            var s = await _shipments.Query().Include(x => x.Events).Include(x => x.Order)
                .Where(x => x.OnlineOrderId == orderId && x.Status != "Cancelled")
                .OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
            return s is null ? null : ToDto(s);
        }

        public async Task<ShipmentListDto> ListShipmentsAsync(string? status, DateTime? from, DateTime? to, string? search, CancellationToken ct = default)
        {
            var q = _shipments.Query().Include(x => x.Order).Include(x => x.Events).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
            {
                q = status switch
                {
                    "open" => q.Where(s => s.Status == "Created" || s.Status == "PickedUp" || s.Status == "InTransit" || s.Status == "OutForDelivery"),
                    "unsettled" => q.Where(s => s.Status == "Delivered" && s.CodAmount > 0 && s.SettledOn == null),
                    _ => q.Where(s => s.Status == status),
                };
            }
            if (from.HasValue) q = q.Where(s => s.CreatedOn >= from.Value);
            if (to.HasValue) q = q.Where(s => s.CreatedOn < to.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var t = search.Trim();
                q = q.Where(s => (s.AwbNumber != null && s.AwbNumber.Contains(t)) || s.Order.Code.Contains(t) || s.Order.CustomerName.Contains(t) || s.Order.CustomerPhone.Contains(t));
            }
            var rows = await q.OrderByDescending(s => s.Id).Take(500).ToListAsync(ct);

            var open = await _shipments.Query().CountAsync(s => s.Status == "Created" || s.Status == "PickedUp" || s.Status == "InTransit" || s.Status == "OutForDelivery", ct);
            var unsettled = await _shipments.Query().Where(s => s.Status == "Delivered" && s.CodAmount > 0 && s.SettledOn == null).ToListAsync(ct);
            return new ShipmentListDto(open, unsettled.Count, unsettled.Sum(s => s.CodAmount), rows.Select(s => ToDto(s)).ToList());
        }

        // ── Create shipment ──────────────────────────────────────────────
        public async Task<(bool ok, string? error, ShipmentDto? shipment)> CreateShipmentForOrderAsync(int orderId, CreateShipmentRequest req, string actor, CancellationToken ct = default)
        {
            var o = await _orders.Query(asNoTracking: false).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == orderId, ct);
            if (o is null) return (false, "Order not found.", null);
            if (!o.IsDelivery) return (false, "This is a pickup order — nothing to ship.", null);
            if (o.Status is "Cancelled") return (false, "Order is cancelled.", null);
            if (o.Status is "New" or "AwaitingPayment") return (false, o.Status == "AwaitingPayment" ? "The customer has not paid yet." : "Accept the order first (that creates the invoice).", null);
            if (string.IsNullOrWhiteSpace(o.AddressLine1) || string.IsNullOrWhiteSpace(o.City)) return (false, "The order has no delivery address.", null);

            var existing = await _shipments.Query(asNoTracking: false).Include(s => s.Events)
                .Where(s => s.OnlineOrderId == orderId && s.Status != "Cancelled" && s.Status != "Failed").OrderByDescending(s => s.Id).FirstOrDefaultAsync(ct);
            if (existing is not null && existing.AwbNumber is not null)
                return (false, $"A shipment already exists for this order (AWB {existing.AwbNumber}).", await GetShipmentAsync(existing.Id, ct));

            if (!await _aramex.IsConfiguredAsync(null, ct)) return (false, "Aramex is not configured — Admin → Integrations.", null);
            var shipper = await ShipperPartyAsync(ct);
            if (shipper is null) return (false, "Shipper profile is incomplete (address line 1 + phone) — Admin → Shipping.", null);

            var env = await _aramex.GetEnvironmentAsync(ct);
            var group = await S("Aramex.ProductGroup", "DOM", ct);
            var type = await S("Aramex.ProductType", "OND", ct);
            var payType = await S("Aramex.PaymentType", "P", ct);
            var codCur = await S("Aramex.CodCurrency", "USD", ct);
            var isCod = o.PaymentMode == "COD";
            var weight = req.WeightKg is > 0 ? req.WeightKg.Value : (o.WeightKg is > 0 ? o.WeightKg.Value : await DefaultWeightKgAsync(ct));
            var pieces = req.Pieces is > 0 ? req.Pieces.Value : 1;
            var reportId = int.TryParse(await S("Aramex.LabelReportId", "9201", ct), out var rid) ? rid : 9201;

            var consignee = new AramexParty(
                null, o.AddressLine1!, o.AddressLine2, o.City!, string.IsNullOrWhiteSpace(o.CountryCode) ? "LB" : o.CountryCode,
                o.CustomerName, o.CustomerName, o.CustomerPhone, o.CustomerPhone, o.CustomerEmail ?? "", o.Code);

            var s = existing ?? new Shipment { OnlineOrderId = o.Id, CreatedBy = actor };
            s.Provider = "Aramex"; s.Environment = env; s.ForeignHawb = o.Code;
            s.ProductGroup = group; s.ProductType = type; s.PaymentType = payType;
            s.Services = isCod ? "CODS" : null;
            s.CodAmount = isCod ? o.Total : 0m; s.CodCurrency = isCod ? codCur : null;
            s.WeightKg = weight; s.Pieces = pieces; s.Status = "Created"; s.Error = null; s.ModifiedOn = DateTime.UtcNow;
            if (existing is null) await _shipments.AddAsync(s, ct);
            await _uow.SaveChangesAsync(ct);

            var comments = $"AXIS order {o.Code}" + (o.DeliveryNotes is { Length: > 0 } dn ? $" · {dn}" : "") + (req.Comments is { Length: > 0 } c ? $" · {c}" : "");
            var res = await _aramex.CreateShipmentAsync(new AramexShipmentRequest(
                o.Code, o.Code, shipper, consignee, weight, pieces, group, type, payType,
                await S("Aramex.DescriptionOfGoods", "Trading cards & gaming accessories", ct), shipper.CountryCode,
                isCod ? o.Total : null, codCur, comments.Length > 200 ? comments[..200] : comments, null, reportId), ct);

            s.RequestJson = res.RawRequest; s.ResponseJson = res.RawResponse; s.ModifiedOn = DateTime.UtcNow;
            if (!res.Ok)
            {
                s.Status = "Failed"; s.Error = Trunc(res.ErrorText, 1000);
                await _uow.SaveChangesAsync(ct);
                _logger.LogWarning("Aramex CreateShipments failed for order {Code}: {Err}", o.Code, res.ErrorText);
                return (false, "Aramex refused the shipment: " + res.ErrorText, await GetShipmentAsync(s.Id, ct));
            }

            s.AwbNumber = res.AwbNumber; s.LabelUrl = res.LabelUrl; s.Status = "Created";
            s.Events.Add(new ShipmentEvent { Code = "AXIS", Description = "Shipment created with Aramex", EventAt = DateTime.UtcNow, Location = shipper.City });
            o.Status = "Shipped"; o.ShippedOn = DateTime.UtcNow; o.HandledBy = actor; o.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation("Aramex shipment {Awb} created for order {Code} ({Env}, COD {Cod})", s.AwbNumber, o.Code, env, s.CodAmount);
            return (true, null, await GetShipmentAsync(s.Id, ct));
        }

        public async Task<(bool ok, string? error, ShipmentDto? shipment)> PrintLabelAsync(int shipmentId, CancellationToken ct = default)
        {
            var s = await _shipments.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == shipmentId, ct);
            if (s is null) return (false, "Shipment not found.", null);
            if (s.AwbNumber is null) return (false, "No AWB yet.", null);
            var creds = await _aramex.LoadCredsAsync(null, ct);
            var reportId = int.TryParse(await S("Aramex.LabelReportId", "9201", ct), out var rid) ? rid : 9201;
            var res = await _aramex.PrintLabelAsync(s.AwbNumber, s.ProductGroup ?? "DOM", creds?.AccountEntity ?? "BEY", reportId, ct);
            if (!res.Ok) return (false, "Aramex could not produce the label: " + res.ErrorText, await GetShipmentAsync(s.Id, ct));
            s.LabelUrl = res.LabelUrl; s.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            return (true, null, await GetShipmentAsync(s.Id, ct));
        }

        // ── Tracking ─────────────────────────────────────────────────────
        public async Task<(bool ok, string? error, ShipmentDto? shipment)> RefreshTrackingAsync(int shipmentId, CancellationToken ct = default)
        {
            var s = await _shipments.Query(asNoTracking: false).Include(x => x.Events).Include(x => x.Order).FirstOrDefaultAsync(x => x.Id == shipmentId, ct);
            if (s is null) return (false, "Shipment not found.", null);
            if (s.AwbNumber is null) return (false, "No AWB to track.", ToDto(s));
            var res = await _aramex.TrackAsync(new[] { s.AwbNumber }, ct);
            s.LastPolledAt = DateTime.UtcNow;
            if (!res.Ok)
            {
                await _uow.SaveChangesAsync(ct);
                return (false, "Aramex tracking failed: " + res.ErrorText, ToDto(s));
            }
            var updates = res.ByAwb.TryGetValue(s.AwbNumber, out var u) ? u : new();
            await ApplyTrackingAsync(s, updates, "aramex-track", ct);
            await _uow.SaveChangesAsync(ct);
            return (true, null, await GetShipmentAsync(s.Id, ct));
        }

        /// <summary>Append new events, move the status forward, fire the delivered hook. Caller saves.</summary>
        private async Task ApplyTrackingAsync(Shipment s, List<AramexTrackingUpdate> updates, string actor, CancellationToken ct)
        {
            var status = s.Status;
            foreach (var u in updates.OrderBy(x => x.At))
            {
                var dup = s.Events.Any(e => e.Code == u.Code && Math.Abs((e.EventAt - u.At).TotalSeconds) < 1);
                if (!dup)
                    s.Events.Add(new ShipmentEvent { Code = Trunc(u.Code, 20), Description = Trunc(u.Description, 300), Location = Trunc(u.Location, 200), Comments = Trunc(u.Comments, 500), EventAt = u.At });
                s.LastTrackingCode = Trunc(u.Code, 20); s.LastTrackingText = Trunc(u.Description, 300); s.LastTrackingAt = u.At;
                if (status is not ("Delivered" or "Returned" or "Cancelled" or "Failed"))
                    status = AramexStatusMap.ToShipmentStatus(u.Code, u.Description, status);
            }
            if (status != s.Status)
            {
                _logger.LogInformation("Shipment {Awb}: {Old} → {New}", s.AwbNumber, s.Status, status);
                s.Status = status; s.ModifiedOn = DateTime.UtcNow;
                if (status == "Delivered") await OnDeliveredAsync(s, actor, ct);
                else if (status == "Returned") await OnReturnedAsync(s, ct);
            }
        }

        public async Task<int> PollOpenShipmentsAsync(CancellationToken ct = default)
        {
            if (!await _aramex.IsConfiguredAsync(null, ct)) return 0;
            var open = await _shipments.Query(asNoTracking: false).Include(x => x.Events).Include(x => x.Order)
                .Where(s => s.AwbNumber != null && (s.Status == "Created" || s.Status == "PickedUp" || s.Status == "InTransit" || s.Status == "OutForDelivery"))
                .OrderBy(s => s.Id).Take(200).ToListAsync(ct);
            if (open.Count == 0) return 0;

            var changed = 0;
            foreach (var chunk in open.Chunk(20))
            {
                var res = await _aramex.TrackAsync(chunk.Select(s => s.AwbNumber!), ct);
                foreach (var s in chunk)
                {
                    s.LastPolledAt = DateTime.UtcNow;
                    if (!res.Ok) continue;
                    var before = s.Status;
                    var ups = res.ByAwb.TryGetValue(s.AwbNumber!, out var u) ? u : new();
                    try { await ApplyTrackingAsync(s, ups, "aramex-poll", ct); }
                    catch (Exception ex) { _logger.LogError(ex, "Tracking apply failed for shipment {Id}", s.Id); }
                    if (s.Status != before) changed++;
                }
                await _uow.SaveChangesAsync(ct);
            }
            return changed;
        }

        public async Task<(bool ok, string? error, ShipmentDto? shipment)> MarkDeliveredAsync(int shipmentId, string actor, CancellationToken ct = default)
        {
            var s = await _shipments.Query(asNoTracking: false).Include(x => x.Events).Include(x => x.Order).FirstOrDefaultAsync(x => x.Id == shipmentId, ct);
            if (s is null) return (false, "Shipment not found.", null);
            if (s.Status == "Delivered") return (true, null, ToDto(s));
            if (s.Status is "Cancelled" or "Failed" or "Returned") return (false, $"Shipment is {s.Status} — create a new shipment instead.", ToDto(s));
            s.Status = "Delivered"; s.ModifiedOn = DateTime.UtcNow;
            s.Events.Add(new ShipmentEvent { Code = "AXIS", Description = $"Marked delivered at the till by {actor}", EventAt = DateTime.UtcNow });
            await OnDeliveredAsync(s, actor, ct);
            await _uow.SaveChangesAsync(ct);
            return (true, null, await GetShipmentAsync(s.Id, ct));
        }

        public async Task<(bool ok, string? error, ShipmentDto? shipment)> MarkFailedAsync(int shipmentId, string status, string? reason, string actor, CancellationToken ct = default)
        {
            if (status is not ("Returned" or "Cancelled" or "Failed")) return (false, "Status must be Returned, Cancelled or Failed.", null);
            var s = await _shipments.Query(asNoTracking: false).Include(x => x.Events).Include(x => x.Order).FirstOrDefaultAsync(x => x.Id == shipmentId, ct);
            if (s is null) return (false, "Shipment not found.", null);
            if (s.Status == "Delivered") return (false, "A delivered shipment cannot be changed — use a refund at the till.", ToDto(s));
            s.Status = status; s.Error = reason is null ? s.Error : Trunc(reason, 1000); s.ModifiedOn = DateTime.UtcNow;
            s.Events.Add(new ShipmentEvent { Code = "AXIS", Description = $"{status} by {actor}" + (reason is { Length: > 0 } ? $": {reason}" : ""), EventAt = DateTime.UtcNow });
            if (status == "Returned") await OnReturnedAsync(s, ct);
            else if (s.Order is not null && s.Order.Status == "Shipped") { s.Order.Status = "Accepted"; s.Order.ShippedOn = null; s.Order.ModifiedOn = DateTime.UtcNow; }
            await _uow.SaveChangesAsync(ct);
            return (true, null, await GetShipmentAsync(s.Id, ct));
        }

        /// <summary>
        /// Delivered: the customer has the goods and, for COD, Aramex has the
        /// cash. Close the open invoice (sale → 1060 via JournalService), book
        /// the delivery fee, stamp the order.
        /// </summary>
        private async Task OnDeliveredAsync(Shipment s, string actor, CancellationToken ct)
        {
            s.DeliveredOn ??= DateTime.UtcNow;
            var o = s.Order ?? await _orders.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == s.OnlineOrderId, ct);
            if (o is null) return;
            o.Status = "Delivered"; o.DeliveredOn ??= DateTime.UtcNow; o.CompletedOn ??= DateTime.UtcNow; o.ModifiedOn = DateTime.UtcNow;

            if (o.PaymentMode == "COD" && o.TransactionRecordId is int txId)
            {
                try
                {
                    var stillOpen = await _tx.Query().Where(t => t.Id == txId).Select(t => t.StatusId != 6).FirstOrDefaultAsync(ct);
                    if (stillOpen)
                    {
                        var trx = _sp.GetRequiredService<ITransactionRecordService>();
                        var res = await trx.CloseOpenInvoice(txId, $"aramex-cod:{actor}", ct);
                        if (!res.Success) _logger.LogError("COD delivered but invoice {Tx} could not be closed: {Err}", txId, res.Error ?? res.Message);
                        _orders.Update(o);              // the till service may reset the tracker
                        _shipments.Update(s);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "COD delivered: closing invoice {Tx} failed for order {Code}", txId, o.Code);
                }
            }

            try { await BookDeliveryFeeCoreAsync(o, ct); }
            catch (Exception ex) { _logger.LogError(ex, "Delivery fee booking failed for order {Code}", o.Code); }
        }

        private Task OnReturnedAsync(Shipment s, CancellationToken ct)
        {
            // Goods came back: the order is not fulfilled. COD invoice stays OPEN
            // (never closed) so the till can void it; a card-paid order needs a
            // refund from the MontyPay portal — flag it, don't guess.
            var o = s.Order;
            if (o is not null)
            {
                o.Status = "Cancelled"; o.CancelReason = $"Returned by courier (AWB {s.AwbNumber})"; o.ModifiedOn = DateTime.UtcNow;
                _logger.LogWarning("Order {Code} returned by Aramex — {Mode} order; review invoice {Tx}", o.Code, o.PaymentMode, o.TransactionRecordId);
            }
            return Task.CompletedTask;
        }

        // ── Pickup ───────────────────────────────────────────────────────
        public async Task<CreatePickupResult> CreatePickupAsync(CreatePickupRequest req, string actor, CancellationToken ct = default)
        {
            var shipper = await ShipperPartyAsync(ct);
            if (shipper is null) return new(false, "Shipper profile is incomplete — Admin → Shipping.", null, null, 0);
            var q = _shipments.Query(asNoTracking: false).Where(s => s.AwbNumber != null && s.Status == "Created" && s.PickupGuid == null);
            if (req.ShipmentIds is { Count: > 0 }) q = q.Where(s => req.ShipmentIds.Contains(s.Id));
            var waiting = await q.ToListAsync(ct);
            if (waiting.Count == 0) return new(false, "No shipments are waiting for pickup.", null, null, 0);

            var day = (req.PickupDate ?? DateTime.UtcNow).Date;
            if (day < DateTime.UtcNow.Date) day = DateTime.UtcNow.Date;
            static DateTime At(DateTime d, string? hhmm, int defH, int defM)
            {
                if (TimeSpan.TryParse(hhmm, out var t)) return DateTime.SpecifyKind(d + t, DateTimeKind.Utc);
                return DateTime.SpecifyKind(d.AddHours(defH).AddMinutes(defM), DateTimeKind.Utc);
            }
            var ready = At(day, req.ReadyTime, 11, 0);
            var last = At(day, req.LastPickupTime, 16, 0);
            var close = At(day, req.ClosingTime, 20, 0);
            if (ready >= last || last > close) return new(false, "Ready time must be before last pickup time, which must be before closing time.", null, null, 0);
            if (day == DateTime.UtcNow.Date && ready < DateTime.UtcNow.AddMinutes(30)) ready = DateTime.UtcNow.AddMinutes(30);

            var res = await _aramex.CreatePickupAsync(new AramexPickupRequest(
                shipper, "Reception", day, ready, last, close,
                req.Reference is { Length: > 0 } r ? r : $"AXIS pickup {day:yyyy-MM-dd}",
                await S("Aramex.ProductGroup", "DOM", ct), await S("Aramex.ProductType", "OND", ct), await S("Aramex.PaymentType", "P", ct),
                waiting.Count, waiting.Sum(s => s.Pieces), waiting.Sum(s => s.WeightKg), $"{waiting.Count} AXIS shipments: " + string.Join(", ", waiting.Select(s => s.AwbNumber))), ct);
            if (!res.Ok) return new(false, "Aramex refused the pickup: " + res.ErrorText, null, null, 0);

            foreach (var s in waiting)
            {
                s.PickupGuid = res.PickupGuid; s.PickupId = res.PickupId; s.ModifiedOn = DateTime.UtcNow;
                s.Events.Add(new ShipmentEvent { Code = "AXIS", Description = $"Courier pickup booked for {day:dd MMM} ({res.PickupId}) by {actor}", EventAt = DateTime.UtcNow });
            }
            await _uow.SaveChangesAsync(ct);
            return new(true, null, res.PickupId, res.PickupGuid, waiting.Count);
        }

        // ── Accounting ───────────────────────────────────────────────────
        private async Task<Account?> AccountAsync(string number, CancellationToken ct)
            => await _accounts.Query().FirstOrDefaultAsync(a => a.AccountNumber == number && a.IsActive, ct);

        public async Task BookDeliveryFeeAsync(int orderId, CancellationToken ct = default)
        {
            var o = await _orders.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == orderId, ct);
            if (o is null) return;
            await BookDeliveryFeeCoreAsync(o, ct);
            await _uow.SaveChangesAsync(ct);
        }

        /// <summary>DR 1060 (COD) | 1050 (card) | 1000 (fallback) / CR 4400 for the fee. Idempotent; caller saves.</summary>
        private async Task BookDeliveryFeeCoreAsync(OnlineOrder o, CancellationToken ct)
        {
            if (o.FeeJournalEntryId.HasValue || o.DeliveryFee <= 0m) return;
            if (o.PaymentMode == "PayAtPickup") return;                      // nothing collected online
            var income = await AccountAsync("4400", ct);
            if (income is null) { _logger.LogWarning("Account 4400 missing — delivery fee for order {Code} not booked", o.Code); return; }
            var debit = o.PaymentMode == "COD" ? await AccountAsync("1060", ct) : await AccountAsync("1050", ct);
            debit ??= await AccountAsync("1000", ct);
            if (debit is null) return;

            var res = await _journal.CreateJournalEntryAsync(new JournalEntryCreateDto(
                DateTime.UtcNow, $"Delivery fee — website order {o.Code}", "OnlineOrderDelivery", o.Id,
                new List<JournalEntryLineCreateDto>
                {
                    new(debit.Id, o.DeliveryFee, 0, o.PaymentMode == "COD" ? "Delivery fee collected by courier (COD)" : "Delivery fee paid online"),
                    new(income.Id, 0, o.DeliveryFee, $"Delivery fee income — {o.City}"),
                }), null, ct);
            if (!res.Success || res.Data is null) { _logger.LogError("Delivery fee JE failed for order {Code}: {Err}", o.Code, res.Error ?? res.Message); return; }
            var post = await _journal.PostJournalEntryAsync(res.Data.Id, null, ct);
            if (!post.Success) _logger.LogError("Delivery fee JE {Id} could not be posted: {Err}", res.Data.Id, post.Error ?? post.Message);
            o.FeeJournalEntryId = res.Data.Id;
        }

        public async Task<CodSettlementResult> SettleCodAsync(CodSettlementRequest req, string actor, CancellationToken ct = default)
        {
            var ids = (req.ShipmentIds ?? new()).Distinct().ToList();
            if (ids.Count == 0) return new(false, "Pick at least one delivered COD shipment.", null, 0, 0, 0, 0);
            var rows = await _shipments.Query(asNoTracking: false).Include(s => s.Order).Where(s => ids.Contains(s.Id)).ToListAsync(ct);
            var bad = rows.Where(s => s.Status != "Delivered" || s.CodAmount <= 0 || s.SettledOn != null).ToList();
            if (bad.Count > 0) return new(false, $"Not settleable: {string.Join(", ", bad.Select(b => b.AwbNumber ?? ("#" + b.Id)))} (must be delivered, COD, and not settled yet).", null, 0, 0, 0, 0);
            if (rows.Count != ids.Count) return new(false, "Some shipments were not found.", null, 0, 0, 0, 0);

            var codTotal = Math.Round(rows.Sum(s => s.CodAmount), 2);
            var net = Math.Round(req.NetReceived ?? codTotal, 2);
            var fees = Math.Round(req.Fees ?? (codTotal - net), 2);
            if (net < 0 || fees < 0) return new(false, "Amounts cannot be negative.", null, codTotal, net, fees, 0);
            if (net + fees != codTotal) return new(false, $"Net received ({net:0.00}) + fees ({fees:0.00}) must equal the COD total ({codTotal:0.00}).", null, codTotal, net, fees, 0);

            var receivable = await AccountAsync("1060", ct);
            if (receivable is null) return new(false, "Account 1060 (Aramex COD Receivable) is missing — run the shipping SQL.", null, codTotal, net, fees, 0);
            var into = await AccountAsync(req.ReceivedInto?.ToLowerInvariant() == "bank" ? "1050" : "1000", ct) ?? await AccountAsync("1000", ct);
            if (into is null) return new(false, "Cash account not found.", null, codTotal, net, fees, 0);
            Account? expense = null;
            if (fees > 0)
            {
                expense = await AccountAsync("5400", ct);
                if (expense is null) return new(false, "Account 5400 (Shipping & Delivery Expense) is missing — run the shipping SQL.", null, codTotal, net, fees, 0);
            }

            var lines = new List<JournalEntryLineCreateDto>();
            if (net > 0) lines.Add(new(into.Id, net, 0, $"Aramex COD remittance{(req.Reference is { Length: > 0 } ? $" ({req.Reference})" : "")}"));
            if (fees > 0) lines.Add(new(expense!.Id, fees, 0, "Aramex freight / COD fees deducted"));
            lines.Add(new(receivable.Id, 0, codTotal, $"COD settled — {rows.Count} shipment(s): {string.Join(", ", rows.Select(s => s.AwbNumber))}"));

            var res = await _journal.CreateJournalEntryAsync(new JournalEntryCreateDto(
                DateTime.UtcNow, $"Aramex COD settlement — {rows.Count} shipment(s), {codTotal:0.00}", "CodSettlement", null, lines), null, ct);
            if (!res.Success || res.Data is null) return new(false, "Journal entry failed: " + (res.Error ?? res.Message), null, codTotal, net, fees, 0);
            var post = await _journal.PostJournalEntryAsync(res.Data.Id, null, ct);
            if (!post.Success) return new(false, "Journal entry created but not posted: " + (post.Error ?? post.Message), res.Data.Id, codTotal, net, fees, 0);

            foreach (var s in rows) { s.SettledOn = DateTime.UtcNow; s.SettlementJournalEntryId = res.Data.Id; s.ModifiedOn = DateTime.UtcNow; }
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation("COD settlement JE {Je}: {Count} shipments, COD {Cod}, net {Net}, fees {Fees} by {Actor}", res.Data.Id, rows.Count, codTotal, net, fees, actor);
            return new(true, null, res.Data.Id, codTotal, net, fees, rows.Count);
        }

        private static string? Trunc(string? s, int n) => s is null ? null : (s.Length <= n ? s : s[..n]);
    }

    /// <summary>Hangfire entry point (registered in Program.cs).</summary>
    public class ShippingJobs
    {
        private readonly IShippingService _shipping;
        private readonly ILogger<ShippingJobs> _logger;
        public ShippingJobs(IShippingService shipping, ILogger<ShippingJobs> logger) { _shipping = shipping; _logger = logger; }

        public async Task PollTrackingAsync(CancellationToken ct)
        {
            try
            {
                var changed = await _shipping.PollOpenShipmentsAsync(ct);
                if (changed > 0) _logger.LogInformation("Aramex poll: {Changed} shipment(s) changed status", changed);
            }
            catch (Exception ex) { _logger.LogError(ex, "Aramex tracking poll failed"); }
        }
    }
}
