using System.Security.Cryptography;
using System.Text.Json;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Domain.Identity;
using Infrastructure.IRepositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    /// <summary>
    /// Website ordering.
    ///
    /// Accounts are the SAME client records the till uses (phone is the key,
    /// role "client"): a customer who was created at the counter just sets a
    /// password the first time; a new visitor registers and immediately
    /// exists for the cashier too (wallet, loyalty, history).
    ///
    /// Orders are priced on the server from the live catalogue (never from
    /// the cart), then:
    ///   • PayAtPickup → status New → the till "accepts" it, which creates a
    ///     normal OPEN invoice (kitchen/bar tickets print through the usual
    ///     path) → closed at pickup like any invoice.
    ///   • Online → status AwaitingPayment + a MontyPay pay link → when the
    ///     payment settles, a PAID invoice is created (accounting + tickets)
    ///     and the order shows up in the inbox as Paid.
    /// </summary>
    public class ShopService : IShopService
    {
        private readonly IBaseRepository<OnlineOrder> _orders;
        private readonly IBaseRepository<OnlineOrderLine> _lines;
        private readonly IBaseRepository<Item> _items;
        private readonly IBaseRepository<ItemAddOn> _addOns;
        private readonly IBaseRepository<ItemVariant> _variants;
        private readonly IBaseRepository<Channel> _channels;
        private readonly IBaseRepository<TransactionRecord> _txRepo;
        private readonly UserManager<AppUser> _users;
        private readonly IAuthService _auth;
        private readonly IWalletService _wallets;
        private readonly IOnlinePaymentService _payments;
        private readonly IUnitOfWork _uow;
        private readonly IServiceProvider _sp;
        private readonly ILogger<ShopService> _logger;

        public ShopService(
            IBaseRepository<OnlineOrder> orders, IBaseRepository<OnlineOrderLine> lines,
            IBaseRepository<Item> items, IBaseRepository<ItemAddOn> addOns, IBaseRepository<ItemVariant> variants,
            IBaseRepository<Channel> channels, IBaseRepository<TransactionRecord> txRepo,
            UserManager<AppUser> users, IAuthService auth, IWalletService wallets, IOnlinePaymentService payments,
            IUnitOfWork uow, IServiceProvider sp, ILogger<ShopService> logger)
        {
            _orders = orders; _lines = lines; _items = items; _addOns = addOns; _variants = variants;
            _channels = channels; _txRepo = txRepo; _users = users; _auth = auth; _wallets = wallets;
            _payments = payments; _uow = uow; _sp = sp; _logger = logger;
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private static string NormalisePhone(string raw) =>
            new string((raw ?? "").Trim().Where(c => char.IsDigit(c) || c == '+').ToArray());

        private static string NewCode()
        {
            const string alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var b = RandomNumberGenerator.GetBytes(6);
            return "AX-" + new string(b.Select(x => alphabet[x % alphabet.Length]).ToArray());
        }

        private async Task<CustomerDto> ToCustomerAsync(AppUser u, CancellationToken ct)
        {
            decimal balance = 0m;
            try { balance = (await _wallets.GetBalancesAsync(new[] { u.Id }, ct)).TryGetValue(u.Id, out var b) ? b : 0m; }
            catch { /* wallet tables may not exist yet */ }
            return new CustomerDto(u.Id, u.FirstName ?? "", u.LastName ?? "", u.PhoneNumber ?? "", u.Email, balance);
        }

        private static OnlineOrderDto ToDto(OnlineOrder o, string? payUrl = null, Dictionary<int, string?>? images = null) => new(
            o.Id, o.Code, o.UserId, o.CustomerName, o.CustomerPhone, o.CustomerEmail,
            o.Fulfilment, o.PaymentMode, o.Status, o.Subtotal, o.Total, o.Notes, o.PickupTime,
            o.TransactionRecordId, o.OnlinePaymentId, payUrl, o.HandledBy, o.CancelReason,
            o.CreatedOn, o.PaidOn, o.AcceptedOn, o.ReadyOn, o.CompletedOn,
            o.Lines.OrderBy(l => l.Id).Select(l => new OnlineOrderLineDto(
                l.Id, l.ItemId, l.ItemName, l.UnitPrice, l.Quantity, l.VariantId, l.VariantName, l.VariantPriceDelta,
                ParseAddOns(l.AddOnsJson), l.LineTotal,
                images != null && images.TryGetValue(l.ItemId, out var img) ? img : null)).ToList(),
            (int)Math.Max(0, (DateTime.UtcNow - o.CreatedOn).TotalMinutes));

        private static List<OrderLineAddOnDto> ParseAddOns(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try
            {
                var rows = JsonSerializer.Deserialize<List<AddOnSnap>>(json) ?? new();
                return rows.Select(r => new OrderLineAddOnDto(r.AddOnId, r.Name, r.Quantity, r.UnitPrice, r.UnitPrice * r.Quantity)).ToList();
            }
            catch { return new(); }
        }
        private sealed record AddOnSnap(int AddOnId, string Name, int Quantity, decimal UnitPrice);

        private async Task<Dictionary<int, string?>> ImagesForAsync(IEnumerable<OnlineOrder> orders, CancellationToken ct)
        {
            var ids = orders.SelectMany(o => o.Lines).Select(l => l.ItemId).Distinct().ToList();
            if (ids.Count == 0) return new();
            return await _items.Query().Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.ImagePath }).ToDictionaryAsync(x => x.Id, x => x.ImagePath, ct);
        }

        // ── Customer auth ────────────────────────────────────────────────
        public async Task<CustomerAuthResponse> RegisterAsync(CustomerRegisterRequest req, CancellationToken ct = default)
        {
            var phone = NormalisePhone(req.Phone);
            if (phone.Length < 6) return new(false, null, null, "Enter a valid phone number.");
            if (string.IsNullOrWhiteSpace(req.FirstName)) return new(false, null, null, "First name is required.");
            if ((req.Password ?? "").Length < 6) return new(false, null, null, "Password must be at least 6 characters.");
            var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim();

            var user = await _users.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone, ct);
            if (user == null && email != null)
                user = await _users.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

            if (user != null)
            {
                // Till-created client without a password → claim the account.
                if (await _users.HasPasswordAsync(user))
                    return new(false, null, null, "This phone / email already has an account. Please sign in.");
                if (!await _users.IsInRoleAsync(user, "client"))
                    return new(false, null, null, "This number belongs to a staff account.");

                var add = await _users.AddPasswordAsync(user, req.Password);
                if (!add.Succeeded) return new(false, null, null, string.Join(" ", add.Errors.Select(e => e.Description)));
                user.FirstName = string.IsNullOrWhiteSpace(user.FirstName) ? req.FirstName.Trim() : user.FirstName;
                user.LastName = string.IsNullOrWhiteSpace(user.LastName) ? (req.LastName ?? "").Trim() : user.LastName;
                user.DisplayName = $"{user.FirstName} {user.LastName}".Trim();
                if (email != null && string.IsNullOrWhiteSpace(user.Email)) user.Email = email;
                await _users.UpdateAsync(user);
            }
            else
            {
                user = new AppUser
                {
                    UserName = phone, PhoneNumber = phone, Email = email,
                    FirstName = req.FirstName.Trim(), LastName = (req.LastName ?? "").Trim(),
                    DisplayName = $"{req.FirstName.Trim()} {(req.LastName ?? "").Trim()}".Trim(),
                    StatusId = (int)UserStatus.Active,
                };
                var created = await _users.CreateAsync(user, req.Password);
                if (!created.Succeeded) return new(false, null, null, string.Join(" ", created.Errors.Select(e => e.Description)));
                var role = await _users.AddToRoleAsync(user, "client");
                if (!role.Succeeded) _logger.LogWarning("Could not add client role to {User}: {Err}", user.Id, string.Join(",", role.Errors.Select(e => e.Description)));
            }

            var token = await _auth.IssueTokenAsync(user, TimeSpan.FromDays(30));
            return new(true, token, await ToCustomerAsync(user, ct), null);
        }

        public async Task<CustomerAuthResponse> LoginAsync(CustomerLoginRequest req, CancellationToken ct = default)
        {
            var id = (req.Identifier ?? "").Trim();
            if (id.Length == 0 || string.IsNullOrEmpty(req.Password)) return new(false, null, null, "Enter your phone or email and password.");

            AppUser? user = null;
            if (id.Contains('@')) user = await _users.FindByEmailAsync(id);
            if (user == null)
            {
                var phone = NormalisePhone(id);
                if (phone.Length >= 6)
                    user = await _users.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone || u.UserName == phone, ct);
            }
            if (user == null) return new(false, null, null, "Invalid credentials.");
            if (user.StatusId != (int)UserStatus.Active) return new(false, null, null, "This account is not active.");
            if (!await _users.HasPasswordAsync(user)) return new(false, null, null, "No password set yet — use Create account with this phone to set one.");
            if (!await _users.CheckPasswordAsync(user, req.Password)) return new(false, null, null, "Invalid credentials.");
            if (!await _users.IsInRoleAsync(user, "client")) return new(false, null, null, "Staff accounts sign in on the dashboard.");

            var token = await _auth.IssueTokenAsync(user, TimeSpan.FromDays(30));
            return new(true, token, await ToCustomerAsync(user, ct), null);
        }

        public async Task<CustomerDto?> MeAsync(int userId, CancellationToken ct = default)
        {
            var u = await _users.FindByIdAsync(userId.ToString());
            return u == null ? null : await ToCustomerAsync(u, ct);
        }

        // ── Checkout ─────────────────────────────────────────────────────
        public async Task<PlaceOrderResult> PlaceOrderAsync(int userId, PlaceOrderRequest req, CancellationToken ct = default)
        {
            var user = await _users.FindByIdAsync(userId.ToString());
            if (user == null) return new(false, null, null, "Please sign in again.");
            var mode = (req.PaymentMode ?? "PayAtPickup").Trim();
            if (mode is not ("PayAtPickup" or "Online")) return new(false, null, null, "Choose how you want to pay.");
            var lines = (req.Lines ?? new()).Where(l => l.Quantity > 0).ToList();
            if (lines.Count == 0) return new(false, null, null, "Your cart is empty.");
            if (mode == "Online" && !await _payments.IsProviderReadyAsync(ct))
                return new(false, null, null, "Online payment is not available right now — choose pay at pickup.");

            var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
            var items = await _items.Query()
                .Include(i => i.AddOns.Where(a => a.IsActive))
                .Include(i => i.Variants.Where(v => v.IsActive))
                .Where(i => itemIds.Contains(i.Id) && i.StatusId == 1)
                .ToDictionaryAsync(i => i.Id, ct);

            var order = new OnlineOrder
            {
                Code = NewCode(), UserId = user.Id,
                CustomerName = $"{user.FirstName} {user.LastName}".Trim().Length > 0 ? $"{user.FirstName} {user.LastName}".Trim() : (user.DisplayName ?? user.PhoneNumber ?? "Customer"),
                CustomerPhone = user.PhoneNumber ?? "", CustomerEmail = user.Email,
                Fulfilment = "Pickup", PaymentMode = mode,
                Status = mode == "Online" ? "AwaitingPayment" : "New",
                Notes = req.Notes?.Trim() is { Length: > 0 } n ? (n.Length > 500 ? n[..500] : n) : null,
                PickupTime = req.PickupTime?.Trim() is { Length: > 0 } p ? (p.Length > 60 ? p[..60] : p) : null,
                CreatedOn = DateTime.UtcNow,
            };

            decimal subtotal = 0m;
            foreach (var l in lines)
            {
                if (!items.TryGetValue(l.ItemId, out var item))
                    return new(false, null, null, "One of the items is no longer available. Please refresh your cart.");

                ItemVariant? variant = null;
                if (item.Variants.Any())
                {
                    variant = item.Variants.FirstOrDefault(v => v.Id == l.VariantId);
                    if (variant == null) return new(false, null, null, $"Please choose a colour / type for {item.Name}.");
                }

                var addOnSnaps = new List<AddOnSnap>();
                foreach (var a in l.AddOns ?? new())
                {
                    if (a.Quantity <= 0) continue;
                    var def = item.AddOns.FirstOrDefault(x => x.Id == a.AddOnId);
                    if (def == null) return new(false, null, null, $"An extra chosen for {item.Name} is no longer available.");
                    addOnSnaps.Add(new AddOnSnap(def.Id, def.Name, a.Quantity, def.Price));
                }
                var addOnsTotal = addOnSnaps.Sum(a => a.UnitPrice * a.Quantity);
                var unit = item.Price + (variant?.PriceDelta ?? 0m);
                var lineTotal = Math.Round(unit * l.Quantity + addOnsTotal, 2);
                subtotal += lineTotal;

                order.Lines.Add(new OnlineOrderLine
                {
                    ItemId = item.Id, ItemName = item.Name, UnitPrice = item.Price, Quantity = l.Quantity,
                    VariantId = variant?.Id, VariantName = variant?.Name, VariantPriceDelta = variant?.PriceDelta ?? 0m,
                    AddOnsJson = addOnSnaps.Count > 0 ? JsonSerializer.Serialize(addOnSnaps) : null,
                    AddOnsTotal = addOnsTotal, LineTotal = lineTotal,
                });
            }
            order.Subtotal = Math.Round(subtotal, 2);
            order.Total = order.Subtotal;

            await _orders.AddAsync(order, ct);
            await _uow.SaveChangesAsync(ct);

            string? payUrl = null;
            if (mode == "Online")
            {
                try
                {
                    var (payment, url) = await _payments.CreateForReferenceAsync(
                        purpose: "OnlineOrder", referenceType: "OnlineOrder", referenceId: order.Id,
                        amount: order.Total, currency: "USD",
                        description: $"AXIS order {order.Code}",
                        customerName: order.CustomerName, customerPhone: order.CustomerPhone, customerEmail: order.CustomerEmail,
                        userId: user.Id, actor: "website", ct: ct);
                    order.OnlinePaymentId = payment.Id;
                    payUrl = url;
                    await _uow.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pay link creation failed for order {Id}; falling back to pay at pickup", order.Id);
                    order.PaymentMode = "PayAtPickup"; order.Status = "New";
                    await _uow.SaveChangesAsync(ct);
                }
            }

            _logger.LogInformation("Website order {Code} placed by user {User}: {Total} ({Mode})", order.Code, user.Id, order.Total, order.PaymentMode);
            return new(true, ToDto(order, payUrl, await ImagesForAsync(new[] { order }, ct)), payUrl, null);
        }

        public async Task<List<OnlineOrderDto>> MyOrdersAsync(int userId, CancellationToken ct = default)
        {
            var rows = await _orders.Query().Include(o => o.Lines)
                .Where(o => o.UserId == userId).OrderByDescending(o => o.Id).Take(50).ToListAsync(ct);
            var imgs = await ImagesForAsync(rows, ct);
            return rows.Select(o => ToDto(o, null, imgs)).ToList();
        }

        public async Task<OnlineOrderDto?> GetMyOrderAsync(int userId, string code, CancellationToken ct = default)
        {
            var o = await _orders.Query().Include(x => x.Lines).FirstOrDefaultAsync(x => x.UserId == userId && x.Code == code, ct);
            if (o == null) return null;
            string? payUrl = null;
            if (o.Status == "AwaitingPayment" && o.OnlinePaymentId.HasValue)
            {
                var p = await _payments.GetAsync(o.OnlinePaymentId.Value, ct);
                payUrl = p?.Payment.PayUrl;
            }
            return ToDto(o, payUrl, await ImagesForAsync(new[] { o }, ct));
        }

        public async Task<bool> CustomerCancelAsync(int userId, string code, CancellationToken ct = default)
        {
            var o = await _orders.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.UserId == userId && x.Code == code, ct);
            if (o == null || o.Status is not ("New" or "AwaitingPayment")) return false;
            o.Status = "Cancelled"; o.CancelReason = "Cancelled by customer"; o.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        // ── Till inbox ───────────────────────────────────────────────────
        public async Task<OnlineOrderInboxDto> InboxAsync(bool includeDone, CancellationToken ct = default)
        {
            var q = _orders.Query().Include(o => o.Lines).AsQueryable();
            if (!includeDone)
                q = q.Where(o => o.Status == "New" || o.Status == "Paid" || o.Status == "Accepted" || o.Status == "Ready" || o.Status == "AwaitingPayment");
            else
            {
                var since = DateTime.UtcNow.AddDays(-2);
                q = q.Where(o => o.CreatedOn >= since);
            }
            var rows = await q.OrderBy(o => o.Status == "New" || o.Status == "Paid" ? 0 : o.Status == "Accepted" ? 1 : o.Status == "Ready" ? 2 : 3)
                .ThenBy(o => o.CreatedOn).ToListAsync(ct);

            // An accepted order whose invoice was closed at the till is done.
            var openTxIds = rows.Where(r => r.Status is "Accepted" or "Ready" && r.TransactionRecordId.HasValue).Select(r => r.TransactionRecordId!.Value).ToList();
            if (openTxIds.Count > 0)
            {
                var closed = await _txRepo.Query().Where(t => openTxIds.Contains(t.Id) && t.StatusId == 6).Select(t => t.Id).ToListAsync(ct);
                if (closed.Count > 0)
                {
                    var tracked = await _orders.Query(asNoTracking: false).Where(o => o.TransactionRecordId != null && closed.Contains(o.TransactionRecordId.Value) && (o.Status == "Accepted" || o.Status == "Ready")).ToListAsync(ct);
                    foreach (var t in tracked) { t.Status = "Completed"; t.CompletedOn = DateTime.UtcNow; t.ModifiedOn = DateTime.UtcNow; }
                    await _uow.SaveChangesAsync(ct);
                    foreach (var r in rows.Where(r => r.TransactionRecordId.HasValue && closed.Contains(r.TransactionRecordId.Value))) { r.Status = "Completed"; r.CompletedOn ??= DateTime.UtcNow; }
                    if (!includeDone) rows = rows.Where(r => r.Status != "Completed").ToList();
                }
            }

            var imgs = await ImagesForAsync(rows, ct);
            return new OnlineOrderInboxDto(
                rows.Count(r => r.Status is "New" or "Paid"),
                rows.Count(r => r.Status == "Accepted"),
                rows.Count(r => r.Status == "Ready"),
                rows.Select(o => ToDto(o, null, imgs)).ToList());
        }

        public async Task<OnlineOrderDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var o = await _orders.Query().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            return o == null ? null : ToDto(o, null, await ImagesForAsync(new[] { o }, ct));
        }

        private List<OrderItemRequest> ToTillLines(OnlineOrder o) =>
            o.Lines.GroupBy(l => l.ItemId).Select(g => new OrderItemRequest(
                g.Key, g.Sum(l => l.Quantity),
                g.SelectMany(l => ParseAddOns(l.AddOnsJson)).GroupBy(a => a.AddOnId)
                    .Select(a => new OrderAddOnRequest(a.Key, a.Sum(x => x.Quantity))).ToList() is { Count: > 0 } ao ? ao : null,
                g.Where(l => l.VariantId.HasValue).GroupBy(l => l.VariantId!.Value)
                    .Select(v => new OrderVariantRequest(v.Key, v.Sum(x => x.Quantity))).ToList() is { Count: > 0 } vr ? vr : null)).ToList();

        private async Task<int?> WebsiteChannelIdAsync(CancellationToken ct)
        {
            try
            {
                return await _channels.Query().Where(c => c.IsActive && (c.Name == "Website" || c.Name == "Online" || c.Name == "Web"))
                    .Select(c => (int?)c.Id).FirstOrDefaultAsync(ct);
            }
            catch { return null; }
        }

        public async Task<(bool ok, string? error, OnlineOrderDto? order)> AcceptAsync(int id, string actor, CancellationToken ct = default)
        {
            var o = await _orders.Query(asNoTracking: false).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o == null) return (false, "Order not found.", null);
            if (o.Status is not ("New" or "Paid")) return (false, $"Order is {o.Status}.", null);

            if (o.TransactionRecordId == null)
            {
                var trx = _sp.GetRequiredService<ITransactionRecordService>();
                var comment = $"Website order {o.Code} · {o.CustomerName} · {o.CustomerPhone}" + (o.PickupTime != null ? $" · pickup {o.PickupTime}" : "") + (o.Notes != null ? $" · {o.Notes}" : "");
                var res = await trx.CreateCoffeeShopOrder(o.UserId, 0, ToTillLines(o), $"web:{actor}", ct,
                    comment: comment, isOpenInvoice: o.Status != "Paid", channelId: await WebsiteChannelIdAsync(ct));
                if (!res.Success || res.Data == null) return (false, res.Error ?? res.Message ?? "Could not create the invoice.", null);
                o.TransactionRecordId = res.Data.Id;
            }
            o.Status = "Accepted"; o.AcceptedOn = DateTime.UtcNow; o.HandledBy = actor; o.ModifiedOn = DateTime.UtcNow;
            _orders.Update(o);   // re-attach in case the till service reset the change tracker
            await _uow.SaveChangesAsync(ct);
            return (true, null, ToDto(o, null, await ImagesForAsync(new[] { o }, ct)));
        }

        public async Task<(bool ok, string? error, OnlineOrderDto? order)> SetStatusAsync(int id, string status, string actor, string? reason, CancellationToken ct = default)
        {
            var o = await _orders.Query(asNoTracking: false).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o == null) return (false, "Order not found.", null);
            switch (status)
            {
                case "Ready":
                    if (o.Status != "Accepted") return (false, "Accept the order first.", null);
                    o.Status = "Ready"; o.ReadyOn = DateTime.UtcNow; break;
                case "Completed":
                    if (o.Status is not ("Accepted" or "Ready")) return (false, $"Order is {o.Status}.", null);
                    o.Status = "Completed"; o.CompletedOn = DateTime.UtcNow; break;
                case "Cancelled":
                    if (o.Status is "Completed" or "Cancelled") return (false, $"Order is already {o.Status}.", null);
                    if (o.TransactionRecordId.HasValue) return (false, "This order already has an invoice — cancel/void the invoice at the till instead.", null);
                    o.Status = "Cancelled"; o.CancelReason = reason; break;
                default:
                    return (false, "Unknown status.", null);
            }
            o.HandledBy = actor; o.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            return (true, null, ToDto(o, null, await ImagesForAsync(new[] { o }, ct)));
        }

        // ── Payments hook ────────────────────────────────────────────────
        /// <summary>
        /// MontyPay settled the order: create the PAID invoice right away
        /// (money is real — accounting + kitchen tickets), then show it in
        /// the inbox as Paid for the till to accept/prepare.
        /// </summary>
        public async Task MarkPaidAsync(int orderId, int onlinePaymentId, CancellationToken ct = default)
        {
            var o = await _orders.Query(asNoTracking: false).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == orderId, ct)
                    ?? throw new InvalidOperationException($"Online order #{orderId} not found.");
            if (o.Status is "Paid" or "Accepted" or "Ready" or "Completed") return;   // idempotent
            if (o.TransactionRecordId.HasValue) { o.Status = "Paid"; o.PaidOn ??= DateTime.UtcNow; o.OnlinePaymentId = onlinePaymentId; await _uow.SaveChangesAsync(ct); return; }
            if (o.Status == "Cancelled")
                _logger.LogWarning("Online order {Code} was cancelled but the card payment settled — reviving it as Paid", o.Code);

            var trx = _sp.GetRequiredService<ITransactionRecordService>();
            var comment = $"Website order {o.Code} · PAID ONLINE · {o.CustomerName} · {o.CustomerPhone}" + (o.PickupTime != null ? $" · pickup {o.PickupTime}" : "") + (o.Notes != null ? $" · {o.Notes}" : "");
            var res = await trx.CreateCoffeeShopOrder(o.UserId, 0, ToTillLines(o), "web:montypay", ct,
                comment: comment, isOpenInvoice: false, channelId: await WebsiteChannelIdAsync(ct));
            if (!res.Success || res.Data == null)
                throw new InvalidOperationException(res.Error ?? res.Message ?? "Could not create the paid invoice for the order.");

            if (Math.Abs(res.Data.TotalPrice - o.Total) > 0.005m)
                _logger.LogWarning("Online order {Code}: invoice total {Inv} differs from amount paid {Paid} (price changed between checkout and settlement)", o.Code, res.Data.TotalPrice, o.Total);

            o.TransactionRecordId = res.Data.Id;
            o.OnlinePaymentId = onlinePaymentId;
            o.Status = "Paid"; o.PaidOn = DateTime.UtcNow; o.ModifiedOn = DateTime.UtcNow; o.CancelReason = null;
            _orders.Update(o);
            await _uow.SaveChangesAsync(ct);
        }
    }
}
