using System.Security.Cryptography;
using Application.DTOs;
using Application.IServices;
using Application.Services.Payments;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    /// <summary>
    /// The Online Payments ledger + orchestration.
    ///
    ///   create link  →  /pay/{code} page  →  gateway session (Redirected)
    ///   → customer pays → gateway callback (source of truth) → Paid
    ///   → fulfil by Purpose (ticket confirmed / wallet credited / invoice closed)
    ///
    /// Gateways are pluggable (<see cref="IOnlinePaymentProvider"/>); the
    /// provider is chosen per payment and stored on the row, so switching
    /// gateways later never rewrites history.
    ///
    /// Callbacks: verified by hash, deduplicated on (provider payment id,
    /// type, status), applied idempotently, and every one is kept verbatim
    /// in OnlinePaymentEvents.
    /// </summary>
    public class OnlinePaymentService : IOnlinePaymentService
    {
        private readonly IBaseRepository<OnlinePayment> _repo;
        private readonly IBaseRepository<OnlinePaymentEvent> _events;
        private readonly IEnumerable<IOnlinePaymentProvider> _providers;
        private readonly IIntegrationSettingsService _settings;
        private readonly IUnitOfWork _uow;
        private readonly IHttpContextAccessor _http;
        private readonly IServiceProvider _sp;   // lazy: fulfilment services would otherwise form a DI cycle
        private readonly ILogger<OnlinePaymentService> _logger;

        private const string DefaultProvider = MontyPayProvider.ProviderKey;
        private static readonly string[] OpenStatuses = { "Created", "Redirected", "Pending" };

        public OnlinePaymentService(
            IBaseRepository<OnlinePayment> repo,
            IBaseRepository<OnlinePaymentEvent> events,
            IEnumerable<IOnlinePaymentProvider> providers,
            IIntegrationSettingsService settings,
            IUnitOfWork uow,
            IHttpContextAccessor http,
            IServiceProvider sp,
            ILogger<OnlinePaymentService> logger)
        {
            _repo = repo;
            _events = events;
            _providers = providers;
            _settings = settings;
            _uow = uow;
            _http = http;
            _sp = sp;
            _logger = logger;
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private IOnlinePaymentProvider? Provider(string key) =>
            _providers.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

        private static string NewCode()
        {
            // 16 chars, URL-safe, ~80 bits — unguessable, short enough for WhatsApp.
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz23456789";
            var bytes = RandomNumberGenerator.GetBytes(16);
            var chars = new char[16];
            for (var i = 0; i < 16; i++) chars[i] = alphabet[bytes[i] % alphabet.Length];
            return new string(chars);
        }

        private async Task<string> PublicBaseAsync(CancellationToken ct) =>
            ((await _settings.GetRawAsync("Payments.PublicBaseUrl", ct))
             ?? (await _settings.GetRawAsync("Event.PublicBaseUrl", ct))
             ?? "https://www.axislb.com").TrimEnd('/');

        private async Task<string> ApiBaseAsync(CancellationToken ct)
        {
            var configured = (await _settings.GetRawAsync("Payments.ApiBaseUrl", ct))?.TrimEnd('/');
            if (!string.IsNullOrWhiteSpace(configured)) return configured;
            var req = _http.HttpContext?.Request;
            return req is null ? "" : $"{req.Scheme}://{req.Host}";
        }

        public string BuildPayUrl(string code, string publicBase) => $"{publicBase.TrimEnd('/')}/pay/{code}";

        private static string CallbackPath(string provider) => $"/api/payments/{provider.ToLowerInvariant()}/callback";

        private static bool IsExpired(OnlinePayment p) =>
            p.ExpiresOn.HasValue && p.ExpiresOn.Value <= DateTime.UtcNow && OpenStatuses.Contains(p.Status);

        private static string? ReferenceLabel(OnlinePayment p) => p.Purpose switch
        {
            "EventTicket" => p.ReferenceId.HasValue ? $"Event registration #{p.ReferenceId}" : "Event ticket",
            "WalletTopUp" => p.ReferenceId.HasValue ? $"Wallet top-up · client #{p.ReferenceId}" : "Wallet top-up",
            "Invoice" => p.ReferenceId.HasValue ? $"Invoice #{p.ReferenceId}" : "Invoice",
            "OnlineOrder" => p.ReferenceId.HasValue ? $"Website order #{p.ReferenceId}" : "Website order",
            _ => null,
        };

        private OnlinePaymentDto ToDto(OnlinePayment p, string publicBase) => new(
            p.Id, p.Code, p.Provider, p.Environment, p.Purpose,
            p.ReferenceType, p.ReferenceId, ReferenceLabel(p),
            p.Amount, p.Currency, p.Description,
            p.CustomerName, p.CustomerPhone, p.CustomerEmail, p.UserId,
            IsExpired(p) ? "Expired" : p.Status,
            p.ProviderOrderNumber, p.ProviderPaymentId, p.ProviderStatus,
            p.PaymentMethodUsed, p.CardMasked, p.FailureReason,
            p.IsFulfilled, p.FulfilledOn, p.FulfillmentError,
            p.CallbackCount, p.LastCallbackOn,
            p.CreatedBy, p.CreatedOn, p.PaidOn, p.ExpiresOn,
            BuildPayUrl(p.Code, publicBase));

        private async Task LogEventAsync(OnlinePayment p, string kind, string? note, string? raw = null,
            string? providerType = null, string? providerStatus = null, string? orderStatus = null,
            string? resultStatus = null, bool hashValid = true, CancellationToken ct = default)
        {
            await _events.AddAsync(new OnlinePaymentEvent
            {
                OnlinePaymentId = p.Id,
                Kind = kind,
                ProviderType = providerType,
                ProviderStatus = providerStatus,
                OrderStatus = orderStatus,
                ResultStatus = resultStatus,
                Note = note is { Length: > 500 } ? note[..500] : note,
                Raw = raw is { Length: > 8000 } ? raw[..8000] : raw,
                HashValid = hashValid,
                CreatedOn = DateTime.UtcNow,
            }, ct);
        }

        // ── Create ───────────────────────────────────────────────────────
        public async Task<OnlinePaymentDto> CreateAsync(OnlinePaymentCreateDto dto, string actor, CancellationToken ct = default)
        {
            if (dto.Amount <= 0) throw new ArgumentException("Amount must be greater than zero.");
            if (string.IsNullOrWhiteSpace(dto.Description)) throw new ArgumentException("Description is required.");

            var purpose = string.IsNullOrWhiteSpace(dto.Purpose) ? "Custom" : dto.Purpose.Trim();
            if (purpose is not ("Custom" or "WalletTopUp" or "Invoice" or "EventTicket" or "OnlineOrder"))
                throw new ArgumentException("Purpose must be Custom, WalletTopUp, Invoice, EventTicket or OnlineOrder.");
            if (purpose == "WalletTopUp" && !(dto.UserId ?? dto.ReferenceId).HasValue)
                throw new ArgumentException("A wallet top-up link needs the client (userId).");
            if (purpose == "Invoice" && !dto.ReferenceId.HasValue)
                throw new ArgumentException("An invoice link needs the invoice id.");

            var providerKey = string.IsNullOrWhiteSpace(dto.Provider) ? DefaultProvider : dto.Provider.Trim();
            var provider = Provider(providerKey) ?? throw new ArgumentException($"Unknown payment provider '{providerKey}'.");

            var p = new OnlinePayment
            {
                Code = NewCode(),
                Provider = provider.Key,
                Environment = await provider.GetEnvironmentAsync(ct),
                Purpose = purpose,
                ReferenceType = purpose switch
                {
                    "WalletTopUp" => "Wallet",
                    "Invoice" => "TransactionRecord",
                    "EventTicket" => "EventRegistration",
                    "OnlineOrder" => "OnlineOrder",
                    _ => dto.ReferenceType,
                },
                ReferenceId = purpose == "WalletTopUp" ? (dto.UserId ?? dto.ReferenceId) : dto.ReferenceId,
                Amount = Math.Round(dto.Amount, 2),
                Currency = string.IsNullOrWhiteSpace(dto.Currency) ? "USD" : dto.Currency.Trim().ToUpperInvariant(),
                Description = dto.Description.Trim().Length > 255 ? dto.Description.Trim()[..255] : dto.Description.Trim(),
                CustomerName = dto.CustomerName?.Trim(),
                CustomerPhone = dto.CustomerPhone?.Trim(),
                CustomerEmail = dto.CustomerEmail?.Trim(),
                UserId = dto.UserId ?? (purpose == "WalletTopUp" ? dto.ReferenceId : null),
                Status = "Created",
                CreatedBy = actor,
                CreatedOn = DateTime.UtcNow,
                ExpiresOn = dto.ExpiresInHours is > 0 ? DateTime.UtcNow.AddHours(dto.ExpiresInHours.Value) : null,
            };

            await _repo.AddAsync(p, ct);
            await _uow.SaveChangesAsync(ct);
            await LogEventAsync(p, "created", $"Created by {actor}", ct: ct);
            await _uow.SaveChangesAsync(ct);

            return ToDto(p, await PublicBaseAsync(ct));
        }

        public async Task<(OnlinePaymentDto Payment, string PayUrl)> CreateForReferenceAsync(
            string purpose, string referenceType, int referenceId, decimal amount, string currency, string description,
            string? customerName, string? customerPhone, string? customerEmail, int? userId, string actor,
            CancellationToken ct = default)
        {
            var dto = await CreateAsync(new OnlinePaymentCreateDto(
                amount, description, currency, customerName, customerPhone, customerEmail, userId,
                purpose, referenceType, referenceId, ExpiresInHours: null), actor, ct);
            return (dto, dto.PayUrl);
        }

        public async Task<bool> IsProviderReadyAsync(CancellationToken ct = default)
        {
            var p = Provider(DefaultProvider);
            return p is not null && await p.IsConfiguredAsync(null, ct);
        }

        // ── Public pay page ──────────────────────────────────────────────
        public async Task<PublicPaymentDto?> GetPublicAsync(string code, CancellationToken ct = default)
        {
            var p = await _repo.Query().FirstOrDefaultAsync(x => x.Code == code, ct);
            if (p is null) return null;
            var expired = IsExpired(p);
            return new PublicPaymentDto(
                p.Code, p.Provider, p.Purpose, p.Amount, p.Currency, p.Description, p.CustomerName,
                expired ? "Expired" : p.Status,
                CanPay: !expired && (OpenStatuses.Contains(p.Status) || p.Status == "Failed"),
                IsExpired: expired, PaidOn: p.PaidOn, ReferenceLabel: ReferenceLabel(p));
        }

        public async Task<PublicPaymentStartResultDto> StartCheckoutAsync(string code, CancellationToken ct = default)
        {
            var p = await _repo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(x => x.Code == code, ct);
            if (p is null) return new PublicPaymentStartResultDto(false, null, "NotFound", "This payment link does not exist.");
            if (p.Status == "Paid") return new PublicPaymentStartResultDto(false, null, "Paid", "This payment has already been made.");
            if (IsExpired(p)) return new PublicPaymentStartResultDto(false, null, "Expired", "This payment link has expired.");
            if (p.Status is "Cancelled" or "Refunded" or "Voided" or "Chargeback")
                return new PublicPaymentStartResultDto(false, null, p.Status, "This payment link is no longer active.");

            var provider = Provider(p.Provider);
            if (provider is null || !await provider.IsConfiguredAsync(p.Environment, ct))
                return new PublicPaymentStartResultDto(false, null, p.Status, "Online payment is not available right now. Please pay at the store.");

            // Re-use a session created in the last 15 minutes (double click / two tabs)
            // instead of minting a new order number every time.
            if (p.Status == "Redirected" && !string.IsNullOrWhiteSpace(p.RedirectUrl)
                && p.ModifiedOn.HasValue && (DateTime.UtcNow - p.ModifiedOn.Value) < TimeSpan.FromMinutes(15))
                return new PublicPaymentStartResultDto(true, p.RedirectUrl, p.Status, null);

            var publicBase = await PublicBaseAsync(ct);
            var apiBase = await ApiBaseAsync(ct);
            var successUrl = $"{publicBase}/pay/{p.Code}/result?outcome=success";
            var cancelUrl = $"{publicBase}/pay/{p.Code}/result?outcome=cancel";
            var notificationUrl = string.IsNullOrEmpty(apiBase) ? "" : apiBase + CallbackPath(p.Provider);

            var session = await provider.CreateCheckoutAsync(p, successUrl, cancelUrl, notificationUrl, ct);
            if (!session.Success)
            {
                p.FailureReason = session.Error is { Length: > 500 } ? session.Error[..500] : session.Error;
                p.ModifiedOn = DateTime.UtcNow;
                await LogEventAsync(p, "redirected", $"Session failed: {session.Error}", session.Raw, resultStatus: null, ct: ct);
                await _uow.SaveChangesAsync(ct);
                return new PublicPaymentStartResultDto(false, null, p.Status, session.Error ?? "Could not start the payment.");
            }

            p.ProviderOrderNumber = session.OrderNumber;
            p.RedirectUrl = session.RedirectUrl is { Length: > 1000 } ? session.RedirectUrl[..1000] : session.RedirectUrl;
            p.Status = "Redirected";
            p.FailureReason = null;
            p.ModifiedOn = DateTime.UtcNow;
            await LogEventAsync(p, "redirected", $"Session {session.OrderNumber} → {p.Provider} ({p.Environment})", session.Raw, resultStatus: "Redirected", ct: ct);
            await _uow.SaveChangesAsync(ct);

            return new PublicPaymentStartResultDto(true, session.RedirectUrl, p.Status, null);
        }

        // ── Callback ─────────────────────────────────────────────────────
        public async Task HandleCallbackAsync(string providerKey, IReadOnlyDictionary<string, string> form, string raw, CancellationToken ct = default)
        {
            try
            {
                var provider = Provider(providerKey);
                if (provider is null)
                {
                    _logger.LogWarning("Payment callback for unknown provider {Provider}", providerKey);
                    return;
                }

                var cb = await provider.ParseCallbackAsync(form, raw, ct);

                OnlinePayment? p = null;
                if (!string.IsNullOrWhiteSpace(cb.OrderNumber))
                    p = await _repo.Query(asNoTracking: false)
                        .FirstOrDefaultAsync(x => x.ProviderOrderNumber == cb.OrderNumber, ct);
                if (p is null && !string.IsNullOrWhiteSpace(cb.ProviderPaymentId))
                    p = await _repo.Query(asNoTracking: false)
                        .FirstOrDefaultAsync(x => x.ProviderPaymentId == cb.ProviderPaymentId, ct);
                // A customer who opened the pay page twice has TWO sessions but
                // one row; the older order number was overwritten. The code is
                // embedded in every order number ("AX-{code}-{stamp}"), so fall
                // back to it — the hash is still verified before anything is applied.
                if (p is null && cb.OrderNumber is { } on && on.StartsWith("AX-", StringComparison.Ordinal))
                {
                    var parts = on.Split('-');
                    if (parts.Length >= 3)
                    {
                        var code = parts[1];
                        p = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Code == code, ct);
                    }
                }

                if (p is null)
                {
                    _logger.LogWarning("{Provider} callback for unknown order {Order} / payment {Pid}: {Raw}",
                        providerKey, cb.OrderNumber, cb.ProviderPaymentId, raw.Length > 500 ? raw[..500] : raw);
                    return;
                }

                p.CallbackCount++;
                p.LastCallbackOn = DateTime.UtcNow;

                if (!cb.HashValid)
                {
                    _logger.LogWarning("{Provider} callback with INVALID hash for payment {Id} — ignored", providerKey, p.Id);
                    await LogEventAsync(p, "callback", "Rejected: hash mismatch", raw,
                        cb.ProviderType, cb.ProviderStatus, cb.OrderStatus, null, hashValid: false, ct: ct);
                    await _uow.SaveChangesAsync(ct);
                    return;
                }

                // Idempotency: same (payment id, type, status) → already applied.
                var dupe = !string.IsNullOrWhiteSpace(cb.ProviderPaymentId)
                    && string.Equals(p.ProviderPaymentId, cb.ProviderPaymentId, StringComparison.Ordinal)
                    && await _events.Query().AnyAsync(e =>
                        e.OnlinePaymentId == p.Id && e.Kind == "callback" && e.HashValid
                        && e.ProviderType == cb.ProviderType && e.ProviderStatus == cb.ProviderStatus
                        && e.OrderStatus == cb.OrderStatus, ct);
                if (dupe)
                {
                    await LogEventAsync(p, "callback", "Duplicate — ignored", raw, cb.ProviderType, cb.ProviderStatus, cb.OrderStatus, null, ct: ct);
                    await _uow.SaveChangesAsync(ct);
                    return;
                }

                // Amount guard: a settled callback for a different amount is
                // recorded but NOT fulfilled.
                var amountMismatch = cb.Outcome == "Paid" && cb.Amount.HasValue && Math.Abs(cb.Amount.Value - p.Amount) > 0.005m;

                await ApplyAsync(p, cb, "callback", amountMismatch, ct);
                await _uow.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                // Callback handlers must never surface an error to the gateway
                // (a non-2xx trips its circuit breaker for ALL merchants on
                // the URL). Log loudly; reconcile picks it up.
                _logger.LogError(ex, "Payment callback processing failed for {Provider}: {Raw}", providerKey, raw.Length > 500 ? raw[..500] : raw);
            }
        }

        /// <summary>Shared state machine for callbacks and status checks. Caller saves.</summary>
        private async Task ApplyAsync(OnlinePayment p, ProviderCallbackResult cb, string kind, bool amountMismatch, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(cb.ProviderPaymentId)) p.ProviderPaymentId = cb.ProviderPaymentId;
            if (!string.IsNullOrWhiteSpace(cb.OrderStatus)) p.ProviderStatus = cb.OrderStatus;
            if (!string.IsNullOrWhiteSpace(cb.PaymentMethod)) p.PaymentMethodUsed = cb.PaymentMethod;
            if (!string.IsNullOrWhiteSpace(cb.CardMasked)) p.CardMasked = cb.CardMasked;
            p.ModifiedOn = DateTime.UtcNow;

            string? result = null;
            string note;

            if (amountMismatch)
            {
                note = $"Settled for {cb.Amount:0.00} {cb.Currency} but link is {p.Amount:0.00} {p.Currency} — NOT fulfilled, check manually";
                p.FailureReason = note;
                p.Status = "Pending";
                result = "Pending";
                _logger.LogWarning("Payment {Id}: {Note}", p.Id, note);
            }
            else switch (cb.Outcome)
            {
                case "Paid":
                    if (p.Status != "Paid")
                    {
                        p.Status = "Paid";
                        p.PaidOn = DateTime.UtcNow;
                        p.FailureReason = null;
                        result = "Paid";
                    }
                    note = $"{cb.ProviderType}/{cb.ProviderStatus} → {cb.OrderStatus}";
                    break;
                case "Failed":
                    // A failed attempt keeps the link usable — the customer may retry.
                    if (p.Status != "Paid") { p.Status = "Failed"; result = "Failed"; }
                    p.FailureReason = cb.Reason is { Length: > 500 } ? cb.Reason[..500] : cb.Reason;
                    note = $"Declined: {cb.Reason}";
                    break;
                case "Refunded":
                case "Voided":
                case "Chargeback":
                    p.Status = cb.Outcome; result = cb.Outcome;
                    note = $"{cb.ProviderType}/{cb.ProviderStatus} → {cb.OrderStatus}";
                    if (p.IsFulfilled)
                    {
                        // The money went back but the ticket / wallet credit /
                        // closed invoice is still in place — that is a manual
                        // decision for the admin, so make it impossible to miss.
                        note += " — money returned by the gateway; the business effect is STILL applied, review manually";
                        p.FulfillmentError = "Reversed at gateway after fulfilment — review manually";
                        _logger.LogWarning("Online payment {Id} ({Purpose}) was {Outcome} after fulfilment", p.Id, p.Purpose, cb.Outcome);
                    }
                    break;
                case "Pending":
                    if (p.Status != "Paid") { p.Status = "Pending"; result = "Pending"; }
                    note = $"Pending: {cb.OrderStatus}";
                    break;
                default:
                    note = $"Intermediate: {cb.ProviderType}/{cb.ProviderStatus} → {cb.OrderStatus}";
                    break;
            }

            await LogEventAsync(p, kind, note, cb.Raw, cb.ProviderType, cb.ProviderStatus, cb.OrderStatus, result, ct: ct);

            if (p.Status == "Paid" && !p.IsFulfilled)
                await FulfilAsync(p, ct);
        }

        // ── Fulfilment ───────────────────────────────────────────────────
        /// <summary>
        /// Applies the business effect once. Never throws — a fulfilment
        /// failure is recorded on the row (FulfillmentError) and surfaced in
        /// the admin page so it can be retried from there.
        /// </summary>
        private async Task FulfilAsync(OnlinePayment p, CancellationToken ct)
        {
            try
            {
                switch (p.Purpose)
                {
                    case "EventTicket":
                    {
                        var events = _sp.GetRequiredService<IEventRegistrationService>();
                        var ok = await events.MarkPaidByProviderRefAsync($"OP:{p.Code}", $"{p.Provider} {p.ProviderPaymentId} settled", ct);
                        if (!ok) throw new InvalidOperationException("Event registration not found for this payment.");
                        break;
                    }
                    case "WalletTopUp":
                    {
                        var userId = p.UserId ?? p.ReferenceId ?? throw new InvalidOperationException("No client on this top-up.");
                        var wallets = _sp.GetRequiredService<IWalletService>();
                        var res = await wallets.TopUpAsync(userId,
                            new WalletTopUpRequestDto(p.Amount, "Online", $"{p.Provider} online top-up · {p.Code}"),
                            $"online:{p.Provider}", ct);
                        if (!res.Success) throw new InvalidOperationException(res.Error ?? res.Message ?? "Wallet top-up failed.");
                        break;
                    }
                    case "Invoice":
                    {
                        var invoiceId = p.ReferenceId ?? throw new InvalidOperationException("No invoice on this payment.");
                        // Never close an invoice for less than it is worth.
                        var trxRepo = _sp.GetRequiredService<IBaseRepository<TransactionRecord>>();
                        var inv = await trxRepo.Query().FirstOrDefaultAsync(t => t.Id == invoiceId, ct)
                                  ?? throw new InvalidOperationException($"Invoice #{invoiceId} not found.");
                        if (inv.StatusId == 6) break; // already closed — nothing to do
                        if (inv.TotalPrice - p.Amount > 0.005m)
                            throw new InvalidOperationException($"Invoice #{invoiceId} is {inv.TotalPrice:0.00} but the link was for {p.Amount:0.00} — close it manually.");
                        var trx = _sp.GetRequiredService<ITransactionRecordService>();
                        var res = await trx.CloseOpenInvoice(invoiceId, $"online:{p.Provider}", ct, 0m);
                        if (!res.Success) throw new InvalidOperationException(res.Error ?? res.Message ?? "Could not close the invoice.");
                        break;
                    }
                    case "OnlineOrder":
                    {
                        var orderId = p.ReferenceId ?? throw new InvalidOperationException("No order on this payment.");
                        var shop = _sp.GetRequiredService<IShopService>();
                        await shop.MarkPaidAsync(orderId, p.Id, ct);
                        break;
                    }
                    default:
                        // Custom links have no automatic effect — the admin sees it Paid.
                        break;
                }

                p.IsFulfilled = true;
                p.FulfilledOn = DateTime.UtcNow;
                p.FulfillmentError = null;
                await LogEventAsync(p, "fulfilled", $"{p.Purpose} applied", resultStatus: "Paid", ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fulfilment failed for online payment {Id} ({Purpose})", p.Id, p.Purpose);
                p.FulfillmentError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                await LogEventAsync(p, "fulfilled", $"FAILED: {ex.Message}", ct: ct);
            }

            // A downstream service may have reset the change tracker (the
            // journal service does on a failed post), which detaches `p` and
            // would silently drop IsFulfilled. Re-attaching is a no-op when
            // it is still tracked.
            _repo.Update(p);
        }

        // ── Admin ────────────────────────────────────────────────────────
        public async Task<OnlinePaymentsPageDto> ListAsync(OnlinePaymentFilterDto f, CancellationToken ct = default)
        {
            static DateTime AsUtc(DateTime d) =>
                d.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(d, DateTimeKind.Utc)
                : d.Kind == DateTimeKind.Local ? d.ToUniversalTime() : d;

            var q = _repo.Query();
            if (f.From.HasValue) { var from = AsUtc(f.From.Value); q = q.Where(p => p.CreatedOn >= from); }
            if (f.To.HasValue) { var to = AsUtc(f.To.Value); q = q.Where(p => p.CreatedOn < to); }
            if (!string.IsNullOrWhiteSpace(f.Purpose)) q = q.Where(p => p.Purpose == f.Purpose);
            if (!string.IsNullOrWhiteSpace(f.Provider)) q = q.Where(p => p.Provider == f.Provider);
            if (!string.IsNullOrWhiteSpace(f.Environment)) q = q.Where(p => p.Environment == f.Environment);
            var now = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(f.Status))
            {
                if (f.Status == "Open")
                    q = q.Where(p => (p.Status == "Created" || p.Status == "Redirected" || p.Status == "Pending")
                                     && (p.ExpiresOn == null || p.ExpiresOn > now));
                else if (f.Status == "Expired")
                    q = q.Where(p => (p.Status == "Created" || p.Status == "Redirected" || p.Status == "Pending")
                                     && p.ExpiresOn != null && p.ExpiresOn <= now);
                else q = q.Where(p => p.Status == f.Status);
            }
            if (!string.IsNullOrWhiteSpace(f.Search))
            {
                var s = f.Search.Trim().ToLower();
                q = q.Where(p =>
                    p.Code.ToLower().Contains(s)
                    || (p.CustomerName != null && p.CustomerName.ToLower().Contains(s))
                    || (p.CustomerPhone != null && p.CustomerPhone.Contains(s))
                    || (p.CustomerEmail != null && p.CustomerEmail.ToLower().Contains(s))
                    || p.Description.ToLower().Contains(s)
                    || (p.ProviderOrderNumber != null && p.ProviderOrderNumber.ToLower().Contains(s))
                    || (p.ProviderPaymentId != null && p.ProviderPaymentId.ToLower().Contains(s)));
            }

            // Summary over the whole filtered set (not the page).
            var stats = await q
                .Select(p => new
                {
                    Status = (p.Status == "Created" || p.Status == "Redirected" || p.Status == "Pending")
                             && p.ExpiresOn != null && p.ExpiresOn <= now ? "Expired" : p.Status,
                    p.Purpose, p.Provider, p.Amount,
                })
                .GroupBy(p => new { p.Status, p.Purpose, p.Provider })
                .Select(g => new { g.Key.Status, g.Key.Purpose, g.Key.Provider, Sum = g.Sum(x => x.Amount), Count = g.Count() })
                .ToListAsync(ct);

            decimal SumWhere(Func<string, bool> st) => stats.Where(x => st(x.Status)).Sum(x => x.Sum);
            int CountWhere(Func<string, bool> st) => stats.Where(x => st(x.Status)).Sum(x => x.Count);
            static bool IsPaid(string s) => s == "Paid";
            static bool IsOpen(string s) => s is "Created" or "Redirected" or "Pending";
            static bool IsFailed(string s) => s is "Failed" or "Cancelled" or "Expired";
            static bool IsRefunded(string s) => s is "Refunded" or "Voided" or "Chargeback";

            var summary = new OnlinePaymentSummaryDto(
                PaidAmount: SumWhere(IsPaid), PaidCount: CountWhere(IsPaid),
                PendingAmount: SumWhere(IsOpen), PendingCount: CountWhere(IsOpen),
                FailedAmount: SumWhere(IsFailed), FailedCount: CountWhere(IsFailed),
                RefundedAmount: SumWhere(IsRefunded), RefundedCount: CountWhere(IsRefunded),
                ByPurpose: stats.Where(x => IsPaid(x.Status)).GroupBy(x => x.Purpose)
                    .Select(g => new OnlinePaymentBucketDto(g.Key, g.Sum(x => x.Sum), g.Sum(x => x.Count))).OrderByDescending(b => b.PaidAmount).ToList(),
                ByProvider: stats.Where(x => IsPaid(x.Status)).GroupBy(x => x.Provider)
                    .Select(g => new OnlinePaymentBucketDto(g.Key, g.Sum(x => x.Sum), g.Sum(x => x.Count))).OrderByDescending(b => b.PaidAmount).ToList());

            var total = await q.CountAsync(ct);
            var page = Math.Max(1, f.Page);
            var size = Math.Clamp(f.PageSize, 1, 200);
            var rows = await q.OrderByDescending(p => p.CreatedOn).ThenByDescending(p => p.Id)
                .Skip((page - 1) * size).Take(size).ToListAsync(ct);

            var publicBase = await PublicBaseAsync(ct);
            return new OnlinePaymentsPageDto(summary, total, rows.Select(r => ToDto(r, publicBase)).ToList(), page, size);
        }

        public async Task<OnlinePaymentDetailDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var p = await _repo.Query().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return null;
            var ev = await _events.Query().Where(e => e.OnlinePaymentId == id).OrderByDescending(e => e.Id).ToListAsync(ct);
            return new OnlinePaymentDetailDto(
                ToDto(p, await PublicBaseAsync(ct)),
                ev.Select(e => new OnlinePaymentEventDto(e.Id, e.Kind, e.ProviderType, e.ProviderStatus, e.OrderStatus,
                    e.ResultStatus, e.Note, e.HashValid, e.CreatedOn, e.Raw)).ToList());
        }

        public async Task<OnlinePaymentDto?> ReconcileAsync(int id, string actor, CancellationToken ct = default)
        {
            var p = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return null;

            // Paid but not fulfilled → retry the business effect without asking the gateway.
            if (p.Status == "Paid" && !p.IsFulfilled)
            {
                await FulfilAsync(p, ct);
                p.ModifiedOn = DateTime.UtcNow;
                await _uow.SaveChangesAsync(ct);
                return ToDto(p, await PublicBaseAsync(ct));
            }

            var provider = Provider(p.Provider);
            if (provider is null) return ToDto(p, await PublicBaseAsync(ct));

            var res = await provider.QueryStatusAsync(p, ct);
            if (res is null)
            {
                await LogEventAsync(p, "status-check", $"No answer from {p.Provider} (by {actor})", ct: ct);
            }
            else if (res.ProviderStatus == "error")
            {
                await LogEventAsync(p, "status-check", $"Gateway error: {res.Reason} (by {actor})", res.Raw, ct: ct);
            }
            else
            {
                var mismatch = res.Outcome == "Paid" && res.Amount.HasValue && Math.Abs(res.Amount.Value - p.Amount) > 0.005m;
                await ApplyAsync(p, res, "status-check", mismatch, ct);
            }
            p.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            return ToDto(p, await PublicBaseAsync(ct));
        }

        public async Task<bool> CancelAsync(int id, string actor, string? reason, CancellationToken ct = default)
        {
            var p = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return false;
            if (p.Status == "Paid") throw new InvalidOperationException("A paid payment cannot be cancelled — refund it from the MontyPay portal.");
            p.Status = "Cancelled";
            p.FailureReason = reason;
            p.ModifiedOn = DateTime.UtcNow;
            await LogEventAsync(p, "manual", $"Cancelled by {actor}: {reason}", resultStatus: "Cancelled", ct: ct);
            await _uow.SaveChangesAsync(ct);
            return true;
        }

        public async Task<PaymentProviderConfigDto> GetProviderConfigAsync(string providerKey, CancellationToken ct = default)
        {
            var provider = Provider(providerKey) ?? throw new ArgumentException($"Unknown provider '{providerKey}'.");
            var env = await provider.GetEnvironmentAsync(ct);
            var apiBase = await ApiBaseAsync(ct);
            var publicBase = await PublicBaseAsync(ct);
            return new PaymentProviderConfigDto(
                Provider: provider.Key,
                Environment: env,
                SandboxConfigured: await provider.IsConfiguredAsync("sandbox", ct),
                ProductionConfigured: await provider.IsConfiguredAsync("production", ct),
                ActiveConfigured: await provider.IsConfiguredAsync(env, ct),
                CallbackUrl: apiBase + CallbackPath(provider.Key),
                SuccessUrlSample: $"{publicBase}/pay/{{code}}/result?outcome=success",
                CancelUrlSample: $"{publicBase}/pay/{{code}}/result?outcome=cancel",
                PublicBaseUrl: publicBase,
                HashAlgorithm: string.Equals(await _settings.GetRawAsync("MontyPay.HashAlgorithm", ct), "sha256", StringComparison.OrdinalIgnoreCase) ? "sha256" : "md5");
        }
    }
}
