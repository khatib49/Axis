using System.Security.Cryptography;
using System.Text;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.OrderingChannels
{
    /// <summary>
    /// Delivery & takeaway operations. Every order is an EXISTING open F&B
    /// invoice (status 7) created through ITransactionRecordService.
    /// CreateCoffeeShopOrder; this service only wraps it with the customer,
    /// zone, driver and status workflow. The delivery fee lives on the
    /// DeliveryOrder (there is no fee line on invoices) and is returned in
    /// the DTO as Total = invoice total + fee.
    /// </summary>
    public class DeliveryService : IDeliveryService
    {
        public const string WebhookSecretKeyPrefix = "Delivery.WebhookSecret.";

        private readonly ApplicationDbContext _db;
        private readonly ITransactionRecordService _trx;
        private readonly ICurrentUserAccessor _user;
        private readonly IIntegrationSettingsService _settings;
        private readonly ILogger<DeliveryService> _logger;

        public DeliveryService(
            ApplicationDbContext db,
            ITransactionRecordService trx,
            ICurrentUserAccessor user,
            IIntegrationSettingsService settings,
            ILogger<DeliveryService> logger)
        {
            _db = db;
            _trx = trx;
            _user = user;
            _settings = settings;
            _logger = logger;
        }

        // ═════════════════════════════ Zones ═════════════════════════════

        private static DeliveryZoneDto ToDto(DeliveryZone z) =>
            new(z.Id, z.Name, z.Fee, z.MinOrder, z.EtaMinutes, z.IsActive, z.BranchId);

        public async Task<List<DeliveryZoneDto>> ListZonesAsync(bool includeInactive, CancellationToken ct = default)
        {
            var q = _db.Set<DeliveryZone>().AsNoTracking();
            if (!includeInactive) q = q.Where(z => z.IsActive);
            return await q.OrderBy(z => z.Name).Select(z => ToDto(z)).ToListAsync(ct);
        }

        public async Task<DeliveryZoneDto?> GetZoneAsync(int id, CancellationToken ct = default)
        {
            var z = await _db.Set<DeliveryZone>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return z is null ? null : ToDto(z);
        }

        private static string? ValidateZone(DeliveryZoneUpsertDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Zone name is required.";
            if (dto.Fee < 0) return "Fee cannot be negative.";
            if (dto.MinOrder < 0) return "Minimum order cannot be negative.";
            if (dto.EtaMinutes < 0) return "ETA cannot be negative.";
            return null;
        }

        public async Task<BaseResponse<DeliveryZoneDto>> CreateZoneAsync(DeliveryZoneUpsertDto dto, CancellationToken ct = default)
        {
            var err = ValidateZone(dto);
            if (err != null) return new(false, "Validation", err);

            var z = new DeliveryZone
            {
                Name = dto.Name.Trim(),
                Fee = dto.Fee,
                MinOrder = dto.MinOrder,
                EtaMinutes = dto.EtaMinutes,
                IsActive = dto.IsActive,
                BranchId = _user.BranchId,
            };
            _db.Add(z);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Zone created.", ToDto(z));
        }

        public async Task<BaseResponse<DeliveryZoneDto>> UpdateZoneAsync(int id, DeliveryZoneUpsertDto dto, CancellationToken ct = default)
        {
            var err = ValidateZone(dto);
            if (err != null) return new(false, "Validation", err);

            var z = await _db.Set<DeliveryZone>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (z is null) return new(false, "NotFound", "Zone not found.");

            z.Name = dto.Name.Trim();
            z.Fee = dto.Fee;
            z.MinOrder = dto.MinOrder;
            z.EtaMinutes = dto.EtaMinutes;
            z.IsActive = dto.IsActive;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Zone updated.", ToDto(z));
        }

        public async Task<bool> DeleteZoneAsync(int id, CancellationToken ct = default)
        {
            var z = await _db.Set<DeliveryZone>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (z is null) return false;
            // Orders keep history (ZoneId → null via SetNull); zones in use are soft-hidden instead.
            var inUse = await _db.Set<DeliveryOrder>().AnyAsync(o => o.ZoneId == id, ct);
            if (inUse) z.IsActive = false; else _db.Remove(z);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // ════════════════════════════ Drivers ════════════════════════════

        private static DriverDto ToDto(Driver d) =>
            new(d.Id, d.Name, d.Phone, d.VehicleNote, d.UserId, d.IsActive, d.BranchId);

        public async Task<List<DriverDto>> ListDriversAsync(bool includeInactive, CancellationToken ct = default)
        {
            var q = _db.Set<Driver>().AsNoTracking();
            if (!includeInactive) q = q.Where(d => d.IsActive);
            return await q.OrderBy(d => d.Name).Select(d => ToDto(d)).ToListAsync(ct);
        }

        public async Task<BaseResponse<DriverDto>> CreateDriverAsync(DriverUpsertDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return new(false, "Validation", "Driver name is required.");
            var d = new Driver
            {
                Name = dto.Name.Trim(),
                Phone = dto.Phone?.Trim(),
                VehicleNote = dto.VehicleNote?.Trim(),
                UserId = dto.UserId is > 0 ? dto.UserId : null,
                IsActive = dto.IsActive,
                BranchId = _user.BranchId,
            };
            _db.Add(d);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Driver created.", ToDto(d));
        }

        public async Task<BaseResponse<DriverDto>> UpdateDriverAsync(int id, DriverUpsertDto dto, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return new(false, "Validation", "Driver name is required.");
            var d = await _db.Set<Driver>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d is null) return new(false, "NotFound", "Driver not found.");
            d.Name = dto.Name.Trim();
            d.Phone = dto.Phone?.Trim();
            d.VehicleNote = dto.VehicleNote?.Trim();
            d.UserId = dto.UserId is > 0 ? dto.UserId : null;
            d.IsActive = dto.IsActive;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Driver updated.", ToDto(d));
        }

        public async Task<bool> DeleteDriverAsync(int id, CancellationToken ct = default)
        {
            var d = await _db.Set<Driver>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (d is null) return false;
            var inUse = await _db.Set<DeliveryOrder>().AnyAsync(o => o.DriverId == id, ct);
            if (inUse) d.IsActive = false; else _db.Remove(d);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // ════════════════════════════ Orders ═════════════════════════════

        private static string NormalizeType(string? type)
        {
            if (string.Equals(type, DeliveryOrderTypes.Takeaway, StringComparison.OrdinalIgnoreCase)) return DeliveryOrderTypes.Takeaway;
            if (string.IsNullOrWhiteSpace(type) || string.Equals(type, DeliveryOrderTypes.Delivery, StringComparison.OrdinalIgnoreCase)) return DeliveryOrderTypes.Delivery;
            return "";
        }

        public Task<BaseResponse<DeliveryOrderDto>> CreateAsync(DeliveryOrderCreateDto dto, CancellationToken ct = default)
            => CreateFromSourceAsync(dto, _user.UserName, null, null, null, ct);

        public async Task<BaseResponse<DeliveryOrderDto>> CreateFromSourceAsync(
            DeliveryOrderCreateDto dto, string createdBy, string? externalSource, string? externalRef, decimal? feeOverride, CancellationToken ct = default)
        {
            var type = NormalizeType(dto.Type);
            if (type == "") return new(false, "Validation", "Type must be Delivery or Takeaway.");
            if (string.IsNullOrWhiteSpace(dto.CustomerName)) return new(false, "Validation", "Customer name is required.");
            if (dto.Items is null || dto.Items.Count == 0) return new(false, "Validation", "At least one item is required.");
            if (dto.Items.Any(i => i.Quantity <= 0)) return new(false, "Validation", "Item quantities must be greater than zero.");
            if (type == DeliveryOrderTypes.Delivery && string.IsNullOrWhiteSpace(dto.Address) && externalSource is null)
                return new(false, "Validation", "Address is required for a delivery order.");

            // Idempotency guard for sourced orders (webhook / kiosk retries).
            if (!string.IsNullOrWhiteSpace(externalSource) && !string.IsNullOrWhiteSpace(externalRef))
            {
                var existing = await _db.Set<DeliveryOrder>().AsNoTracking()
                    .FirstOrDefaultAsync(o => o.ExternalSource == externalSource && o.ExternalRef == externalRef, ct);
                if (existing != null)
                    return new(true, null, "Order already exists.", await GetAsync(existing.Id, ct));
            }

            // Zone + minimum order (uses current item prices, pre-discount).
            DeliveryZone? zone = null;
            decimal fee = feeOverride ?? 0m;
            if (type == DeliveryOrderTypes.Delivery && dto.ZoneId is > 0)
            {
                zone = await _db.Set<DeliveryZone>().AsNoTracking().FirstOrDefaultAsync(z => z.Id == dto.ZoneId, ct);
                if (zone is null) return new(false, "Validation", "Delivery zone not found.");
                if (!zone.IsActive) return new(false, "Validation", $"Zone '{zone.Name}' is not active.");

                var subtotal = await EstimateSubtotalAsync(dto.Items, ct);
                if (subtotal < zone.MinOrder)
                    return new(false, "MinOrder",
                        $"Minimum order for zone '{zone.Name}' is {zone.MinOrder:0.00}; cart is {subtotal:0.00}.");

                fee = feeOverride ?? zone.Fee;
            }
            if (type == DeliveryOrderTypes.Takeaway && feeOverride is null) fee = 0m;

            var comment = BuildComment(type, dto, externalSource, externalRef);

            var created = await _trx.CreateCoffeeShopOrder(
                dto.UserId, dto.DiscountId, dto.Items, createdBy, ct,
                comment: comment, isOpenInvoice: true, setId: null, channelId: dto.ChannelId);

            if (!created.Success || created.Data is null)
                return new(false, created.Error ?? "OrderFailed", created.Message);

            var order = new DeliveryOrder
            {
                TransactionId = created.Data.Id,
                Type = type,
                ZoneId = zone?.Id,
                CustomerName = dto.CustomerName.Trim(),
                Phone = dto.Phone?.Trim(),
                Address = dto.Address?.Trim(),
                Notes = dto.Notes?.Trim(),
                DeliveryFee = Math.Round(fee, 2),
                Status = DeliveryStatuses.Pending,
                ScheduledFor = dto.ScheduledFor,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = createdBy,
                ExternalSource = externalSource,
                ExternalRef = externalRef,
                BranchId = _user.BranchId,
            };
            order.StatusEvents.Add(new DeliveryStatusEvent
            {
                FromStatus = null,
                ToStatus = DeliveryStatuses.Pending,
                On = order.CreatedOn,
                By = createdBy,
                Note = externalSource is null ? "Created at till" : $"Created from {externalSource}",
            });
            _db.Add(order);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Delivery order {OrderId} ({Type}) created on invoice {TrxId} by {By} source={Source}",
                order.Id, type, order.TransactionId, createdBy, externalSource ?? "till");

            return new(true, null, "Order created.", await GetAsync(order.Id, ct));
        }

        private static string BuildComment(string type, DeliveryOrderCreateDto dto, string? source, string? externalRef)
        {
            var sb = new StringBuilder();
            sb.Append(type).Append(": ").Append(dto.CustomerName.Trim());
            if (!string.IsNullOrWhiteSpace(dto.Phone)) sb.Append('/').Append(dto.Phone.Trim());
            if (type == DeliveryOrderTypes.Delivery && !string.IsNullOrWhiteSpace(dto.Address)) sb.Append('/').Append(dto.Address.Trim());
            if (!string.IsNullOrWhiteSpace(source)) sb.Append(" | ").Append(source).Append(externalRef is null ? "" : " #" + externalRef);
            if (!string.IsNullOrWhiteSpace(dto.Notes)) sb.Append(" | ").Append(dto.Notes.Trim());
            return sb.ToString();
        }

        private async Task<decimal> EstimateSubtotalAsync(List<OrderItemRequest> items, CancellationToken ct)
        {
            var qty = items.GroupBy(i => i.ItemId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
            var ids = qty.Keys.ToList();
            var prices = await _db.Set<Item>().AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.Price })
                .ToListAsync(ct);
            return prices.Sum(p => p.Price * qty[p.Id]);
        }

        public async Task<DeliveryOrderDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var o = await OrdersQuery().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null) return null;
            return (await MapAsync(new List<DeliveryOrder> { o }, ct)).FirstOrDefault();
        }

        private IQueryable<DeliveryOrder> OrdersQuery() =>
            _db.Set<DeliveryOrder>().AsNoTracking()
                .Include(o => o.Zone)
                .Include(o => o.Driver)
                .Include(o => o.StatusEvents);

        private async Task<List<DeliveryOrderDto>> MapAsync(List<DeliveryOrder> orders, CancellationToken ct)
        {
            if (orders.Count == 0) return new List<DeliveryOrderDto>();
            var trxIds = orders.Select(o => o.TransactionId).Distinct().ToList();
            var trxs = await _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => trxIds.Contains(t.Id))
                .Include(t => t.Channel)
                .Include(t => t.TransactionItems).ThenInclude(ti => ti.Item)
                .ToDictionaryAsync(t => t.Id, ct);

            return orders.Select(o =>
            {
                trxs.TryGetValue(o.TransactionId, out var t);
                var lines = t?.TransactionItems
                    .Select(ti => new DeliveryOrderLineDto(
                        ti.ItemId,
                        ti.Item?.Name ?? $"#{ti.ItemId}",
                        ti.Quantity,
                        ti.Item?.Price ?? 0m,
                        ti.IsIncluded ? 0m : (ti.Item?.Price ?? 0m) * ti.Quantity))
                    .ToList() ?? new List<DeliveryOrderLineDto>();
                var subtotal = t?.TotalPrice ?? 0m;
                return new DeliveryOrderDto(
                    o.Id, o.TransactionId, o.Type,
                    o.ZoneId, o.Zone?.Name,
                    o.DriverId, o.Driver?.Name,
                    o.CustomerName, o.Phone, o.Address, o.Notes,
                    o.DeliveryFee, subtotal, Math.Round(subtotal + o.DeliveryFee, 2),
                    o.Status, o.ScheduledFor, o.CreatedOn, o.CreatedBy,
                    o.AssignedOn, o.PickedUpOn, o.DeliveredOn, o.CancelledReason,
                    o.ExternalSource, o.ExternalRef,
                    t?.ChannelId, t?.Channel?.Name,
                    t?.StatusId == 6,
                    lines,
                    o.StatusEvents.OrderBy(e => e.On).ThenBy(e => e.Id)
                        .Select(e => new DeliveryStatusEventDto(e.Id, e.FromStatus, e.ToStatus, e.On, e.By, e.Note)).ToList());
            }).ToList();
        }

        /// <summary>UTC instant of today's Beirut midnight (business day start).</summary>
        private static DateTime TodayStartUtc()
        {
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessDate.Zone);
            var localMidnight = DateTime.SpecifyKind(localNow.Date, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(localMidnight, BusinessDate.Zone);
        }

        public async Task<DeliveryBoardDto> GetBoardAsync(CancellationToken ct = default)
        {
            var since = TodayStartUtc();
            var active = new[] { DeliveryStatuses.Pending, DeliveryStatuses.Preparing, DeliveryStatuses.Ready, DeliveryStatuses.OutForDelivery };
            var q = OrdersQuery().Where(o => active.Contains(o.Status) || o.CreatedOn >= since);
            // Phase 2 (branches): when the dashboard sends X-Branch-Id, show that
            // branch's orders plus rows that were never stamped with a branch.
            var branchId = _user.BranchId;
            if (branchId.HasValue) q = q.Where(o => o.BranchId == null || o.BranchId == branchId.Value);
            var orders = await q.OrderBy(o => o.CreatedOn).ToListAsync(ct);
            var dtos = await MapAsync(orders, ct);
            var columns = DeliveryStatuses.All
                .Select(s => new DeliveryBoardColumnDto(s, dtos.Where(d => d.Status == s).ToList()))
                .ToList();
            return new DeliveryBoardDto(columns, DateTime.UtcNow);
        }

        public async Task<BaseResponse<DeliveryOrderDto>> AssignDriverAsync(int id, int driverId, CancellationToken ct = default)
        {
            var o = await _db.Set<DeliveryOrder>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null) return new(false, "NotFound", "Order not found.");
            if (DeliveryStatuses.IsTerminal(o.Status)) return new(false, "Validation", $"Order is {o.Status}; cannot assign a driver.");
            if (o.Type != DeliveryOrderTypes.Delivery) return new(false, "Validation", "Only delivery orders take a driver.");

            var d = await _db.Set<Driver>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == driverId, ct);
            if (d is null) return new(false, "Validation", "Driver not found.");
            if (!d.IsActive) return new(false, "Validation", $"Driver '{d.Name}' is not active.");

            o.DriverId = d.Id;
            o.AssignedOn = DateTime.UtcNow;
            _db.Add(new DeliveryStatusEvent
            {
                DeliveryOrderId = o.Id, FromStatus = o.Status, ToStatus = o.Status,
                On = o.AssignedOn.Value, By = _user.UserName, Note = $"Driver assigned: {d.Name}",
            });
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Driver assigned.", await GetAsync(o.Id, ct));
        }

        public async Task<BaseResponse<DeliveryOrderDto>> AdvanceAsync(int id, string? toStatus, string? note, CancellationToken ct = default)
        {
            var o = await _db.Set<DeliveryOrder>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null) return new(false, "NotFound", "Order not found.");

            var to = string.IsNullOrWhiteSpace(toStatus) ? DeliveryStatusRules.DefaultNext(o.Type, o.Status) : toStatus.Trim();
            if (to is null) return new(false, "Validation", $"Order is already {o.Status}.");
            if (!DeliveryStatusRules.IsKnown(to)) return new(false, "Validation", $"Unknown status '{to}'.");
            if (!DeliveryStatusRules.CanAdvance(o.Type, o.Status, to))
                return new(false, "Transition",
                    $"Cannot move a {o.Type} order from {o.Status} to {to}. Allowed: {string.Join(", ", DeliveryStatusRules.AllowedNext(o.Type, o.Status))}.");
            if (to == DeliveryStatuses.OutForDelivery && o.DriverId is null)
                return new(false, "Validation", "Assign a driver before sending the order out.");

            var now = DateTime.UtcNow;
            var from = o.Status;
            o.Status = to;
            if (to == DeliveryStatuses.OutForDelivery) o.PickedUpOn = now;
            if (to == DeliveryStatuses.Delivered)
            {
                o.DeliveredOn = now;
                o.PickedUpOn ??= now; // takeaway handed over at the counter
            }
            _db.Add(new DeliveryStatusEvent
            {
                DeliveryOrderId = o.Id, FromStatus = from, ToStatus = to, On = now, By = _user.UserName, Note = note?.Trim(),
            });
            await _db.SaveChangesAsync(ct);
            return new(true, null, $"Order is now {to}.", await GetAsync(o.Id, ct));
        }

        public async Task<BaseResponse<DeliveryOrderDto>> CancelAsync(int id, string? reason, CancellationToken ct = default)
        {
            var o = await _db.Set<DeliveryOrder>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null) return new(false, "NotFound", "Order not found.");
            if (!DeliveryStatusRules.CanCancel(o.Status)) return new(false, "Transition", $"Order is already {o.Status}.");

            var now = DateTime.UtcNow;
            var from = o.Status;
            o.Status = DeliveryStatuses.Cancelled;
            o.CancelledReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            _db.Add(new DeliveryStatusEvent
            {
                DeliveryOrderId = o.Id, FromStatus = from, ToStatus = DeliveryStatuses.Cancelled, On = now, By = _user.UserName, Note = o.CancelledReason,
            });
            await _db.SaveChangesAsync(ct);
            // The open invoice is left as-is for the till to close / void with the existing tools.
            return new(true, null, "Order cancelled.", await GetAsync(o.Id, ct));
        }

        public async Task<List<DeliveryOrderDto>> MyOrdersAsync(CancellationToken ct = default)
        {
            var uid = _user.UserId;
            if (uid is null) return new List<DeliveryOrderDto>();
            var driverIds = await _db.Set<Driver>().AsNoTracking()
                .Where(d => d.UserId == uid && d.IsActive).Select(d => d.Id).ToListAsync(ct);
            if (driverIds.Count == 0) return new List<DeliveryOrderDto>();

            var since = TodayStartUtc();
            var orders = await OrdersQuery()
                .Where(o => o.DriverId != null && driverIds.Contains(o.DriverId.Value)
                            && (o.Status == DeliveryStatuses.Ready || o.Status == DeliveryStatuses.OutForDelivery
                                || o.Status == DeliveryStatuses.Preparing || o.Status == DeliveryStatuses.Pending
                                || (o.Status == DeliveryStatuses.Delivered && o.DeliveredOn >= since)))
                .OrderBy(o => o.Status == DeliveryStatuses.OutForDelivery ? 0 : o.Status == DeliveryStatuses.Ready ? 1 : 2)
                .ThenBy(o => o.CreatedOn)
                .ToListAsync(ct);
            return await MapAsync(orders, ct);
        }

        public async Task<DeliveryTodayStatsDto> GetTodayStatsAsync(CancellationToken ct = default)
        {
            var since = TodayStartUtc();
            var orders = await _db.Set<DeliveryOrder>().AsNoTracking()
                .Where(o => o.CreatedOn >= since)
                .ToListAsync(ct);
            var trxIds = orders.Select(o => o.TransactionId).Distinct().ToList();
            var totals = await _db.Set<TransactionRecord>().AsNoTracking()
                .Where(t => trxIds.Contains(t.Id))
                .Select(t => new { t.Id, t.TotalPrice })
                .ToDictionaryAsync(t => t.Id, t => t.TotalPrice, ct);

            int Count(string s) => orders.Count(o => o.Status == s);
            var live = orders.Where(o => o.Status != DeliveryStatuses.Cancelled).ToList();
            var delivered = orders.Where(o => o.Status == DeliveryStatuses.Delivered && o.DeliveredOn != null).ToList();

            return new DeliveryTodayStatsDto(
                Day: BusinessDate.ToDay(DateTime.UtcNow),
                Total: orders.Count,
                Pending: Count(DeliveryStatuses.Pending),
                Preparing: Count(DeliveryStatuses.Preparing),
                Ready: Count(DeliveryStatuses.Ready),
                OutForDelivery: Count(DeliveryStatuses.OutForDelivery),
                Delivered: Count(DeliveryStatuses.Delivered),
                Cancelled: Count(DeliveryStatuses.Cancelled),
                DeliveryCount: orders.Count(o => o.Type == DeliveryOrderTypes.Delivery),
                TakeawayCount: orders.Count(o => o.Type == DeliveryOrderTypes.Takeaway),
                Revenue: live.Sum(o => totals.TryGetValue(o.TransactionId, out var p) ? p : 0m),
                Fees: live.Sum(o => o.DeliveryFee),
                AvgMinutesToDelivered: delivered.Count == 0 ? null
                    : Math.Round(delivered.Average(o => (o.DeliveredOn!.Value - o.CreatedOn).TotalMinutes), 1));
        }

        // ═══════════════════════ Aggregator webhook ═══════════════════════

        private static string NormalizeSource(string source) => (source ?? "").Trim().ToLowerInvariant();

        private static bool SecretMatches(string expected, string? provided)
        {
            if (string.IsNullOrEmpty(provided)) return false;
            var a = Encoding.UTF8.GetBytes(expected.Trim());
            var b = Encoding.UTF8.GetBytes(provided.Trim());
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        public async Task<BaseResponse<WebhookOrderResult>> IngestWebhookAsync(string source, string? providedSecret, WebhookOrderPayload payload, CancellationToken ct = default)
        {
            var src = NormalizeSource(source);
            if (src.Length == 0 || src.Length > 60 || !src.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                return new(false, "Validation", "Invalid source name.");

            var expected = await _settings.GetRawAsync(WebhookSecretKeyPrefix + src, ct);
            if (string.IsNullOrWhiteSpace(expected))
                return new(false, "NotConfigured", $"No webhook secret configured for '{src}' (IntegrationSettings key '{WebhookSecretKeyPrefix}{src}').");
            if (!SecretMatches(expected, providedSecret))
            {
                _logger.LogWarning("Webhook {Source}: bad or missing X-Webhook-Secret", src);
                return new(false, "Unauthorized", "Invalid webhook secret.");
            }

            if (payload is null) return new(false, "Validation", "Empty payload.");
            if (string.IsNullOrWhiteSpace(payload.ExternalRef)) return new(false, "Validation", "externalRef is required.");
            if (payload.Items is null || payload.Items.Count == 0) return new(false, "Validation", "At least one item is required.");
            var externalRef = payload.ExternalRef.Trim();

            // Idempotent on (source, externalRef): replay returns the first order.
            var existing = await _db.Set<DeliveryOrder>().AsNoTracking()
                .FirstOrDefaultAsync(o => o.ExternalSource == src && o.ExternalRef == externalRef, ct);
            if (existing != null)
            {
                var dto = await GetAsync(existing.Id, ct);
                return new(true, null, "Duplicate delivery ignored.", new WebhookOrderResult(true, dto!));
            }

            // Resolve lines: by ItemId, or by sku == item name (case-insensitive).
            var lines = new Dictionary<int, int>();
            foreach (var li in payload.Items)
            {
                if (li.Quantity <= 0) return new(false, "Validation", "Item quantities must be greater than zero.");
                int? itemId = li.ItemId is > 0 ? li.ItemId : null;
                if (itemId is null)
                {
                    if (string.IsNullOrWhiteSpace(li.Sku)) return new(false, "Validation", "Each item needs itemId or sku.");
                    var sku = li.Sku.Trim().ToLower();
                    itemId = await _db.Set<Item>().AsNoTracking()
                        .Where(i => i.Name.ToLower() == sku).Select(i => (int?)i.Id).FirstOrDefaultAsync(ct);
                    if (itemId is null) return new(false, "Validation", $"Unknown item '{li.Sku}'.");
                }
                lines[itemId.Value] = lines.TryGetValue(itemId.Value, out var cur) ? cur + li.Quantity : li.Quantity;
            }

            // Sales channel named after the source (e.g. "toters"), when one exists.
            var channelId = await _db.Set<Channel>().AsNoTracking()
                .Where(c => c.Name.ToLower() == src).Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);

            var create = new DeliveryOrderCreateDto(
                Type: NormalizeType(payload.Type) == DeliveryOrderTypes.Takeaway ? DeliveryOrderTypes.Takeaway : DeliveryOrderTypes.Delivery,
                CustomerName: string.IsNullOrWhiteSpace(payload.CustomerName) ? $"{src} customer" : payload.CustomerName,
                Phone: payload.Phone,
                Address: payload.Address,
                Notes: payload.Note,
                Items: lines.Select(kv => new OrderItemRequest(kv.Key, kv.Value)).ToList(),
                ZoneId: null,
                ChannelId: channelId);

            try
            {
                var r = await CreateFromSourceAsync(create, $"webhook:{src}", src, externalRef, payload.Fee < 0 ? 0 : payload.Fee, ct);
                if (!r.Success || r.Data is null) return new(false, r.Error, r.Message);
                return new(true, null, "Order created.", new WebhookOrderResult(false, r.Data));
            }
            catch (DbUpdateException ex)
            {
                // Concurrent replay hit the unique (source, ref) index: return the winner.
                _logger.LogWarning(ex, "Webhook {Source} #{Ref}: concurrent duplicate", src, externalRef);
                var winner = await _db.Set<DeliveryOrder>().AsNoTracking()
                    .FirstOrDefaultAsync(o => o.ExternalSource == src && o.ExternalRef == externalRef, ct);
                if (winner is null) throw;
                return new(true, null, "Duplicate delivery ignored.", new WebhookOrderResult(true, (await GetAsync(winner.Id, ct))!));
            }
        }
    }
}
