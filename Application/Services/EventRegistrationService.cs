using Application.DTOs;
using Application.IServices;
using Application.Services.Payments;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class EventRegistrationService : IEventRegistrationService
    {
        private readonly IBaseRepository<EventRegistration> _repo;
        // Events are admin-managed now — price, enabled payment methods,
        // WhatsApp number and message template all come from this row
        // rather than from global settings.
        private readonly IBaseRepository<Event> _eventRepo;
        private readonly IIntegrationSettingsService _settings;
        private readonly StripeGateway _stripe;
        private readonly WhishGateway _whish;
        // Ticket money is real revenue, so a confirmed registration posts a
        // journal entry (DR 1000 Cash / CR 4300 Event Revenue) exactly like a
        // closed sale does.
        private readonly IJournalService _journal;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<EventRegistrationService> _logger;
        // Card payments ride the Online Payments module (MontyPay) when it is
        // configured; Stripe stays as the fallback for "Visa".
        private readonly IOnlinePaymentService _onlinePayments;

        public EventRegistrationService(
            IBaseRepository<EventRegistration> repo,
            IBaseRepository<Event> eventRepo,
            IIntegrationSettingsService settings,
            StripeGateway stripe,
            WhishGateway whish,
            IJournalService journal,
            IUnitOfWork uow,
            ILogger<EventRegistrationService> logger,
            IOnlinePaymentService onlinePayments)
        {
            _repo = repo;
            _eventRepo = eventRepo;
            _settings = settings;
            _stripe = stripe;
            _whish = whish;
            _journal = journal;
            _uow = uow;
            _logger = logger;
            _onlinePayments = onlinePayments;
        }

        // ── Public config ────────────────────────────────────────────────
        public async Task<EventPublicConfigDto> GetPublicConfigAsync(string eventKey, CancellationToken ct = default)
        {
            var ev = await _eventRepo.Query().FirstOrDefaultAsync(e => e.Key == eventKey, ct);

            // A method shows up only when the admin enabled it AND the
            // gateway has credentials configured.
            // "Visa" (cards) is available through MontyPay OR Stripe.
            var stripeOk = await _stripe.IsConfiguredAsync(ct) || await _onlinePayments.IsProviderReadyAsync(ct);
            var whishOk = await _whish.IsConfiguredAsync(ct);

            return new EventPublicConfigDto(
                EventKey: eventKey,
                Price: ev?.Price ?? 0m,
                Currency: ev?.Currency ?? "USD",
                WhatsAppNumber: ev?.WhatsAppNumber,
                StripeEnabled: (ev?.EnableVisa ?? false) && stripeOk,
                // Whish counts as available through EITHER the Collect API or
                // a plain payment link on the event.
                WhishEnabled: (ev?.EnableWhish ?? false)
                              && (whishOk || !string.IsNullOrWhiteSpace(ev?.WhishPaymentLink)));
        }

        // ── Register ─────────────────────────────────────────────────────
        public async Task<EventRegisterResultDto> RegisterAsync(EventRegisterRequestDto dto, CancellationToken ct = default)
        {
            // Validate
            if (string.IsNullOrWhiteSpace(dto.FirstName) || string.IsNullOrWhiteSpace(dto.LastName))
                throw new ArgumentException("First and last name are required.");
            if (string.IsNullOrWhiteSpace(dto.Phone))
                throw new ArgumentException("Phone number is required.");

            var method = (dto.PaymentMethod ?? "").Trim();
            if (method is not ("Visa" or "Whish" or "Cash"))
                throw new ArgumentException("Payment method must be Visa, Whish, or Cash.");

            var phone = NormalisePhone(dto.Phone);
            var eventKey = string.IsNullOrWhiteSpace(dto.EventKey) ? "squid-game-x-axis" : dto.EventKey.Trim();

            // The event row is the source of truth for price, which payment
            // methods are offered, capacity and the WhatsApp template.
            var ev = await _eventRepo.Query().FirstOrDefaultAsync(e => e.Key == eventKey, ct)
                     ?? throw new ArgumentException("This event does not exist.");
            if (!ev.IsActive || !ev.IsPublished)
                throw new InvalidOperationException("Registration for this event is closed.");

            // Reject a method the admin switched off for this event.
            var methodEnabled = method switch
            {
                "Visa" => ev.EnableVisa,
                "Whish" => ev.EnableWhish,
                "Cash" => ev.EnableCash,
                _ => false,
            };
            if (!methodEnabled)
                throw new InvalidOperationException($"{method} is not available for this event.");

            // Capacity is counted on PAID registrations only — pending ones
            // may never convert and shouldn't hold a seat hostage.
            if (ev.Capacity is > 0)
            {
                var paidSoFar = await _repo.Query()
                    .CountAsync(r => r.EventId == ev.Id && r.PaymentStatus == "Paid", ct);
                if (paidSoFar >= ev.Capacity.Value)
                    throw new InvalidOperationException("This event is sold out.");
            }

            // Block a second PAID registration on the same phone. A pending
            // one is fine — the buyer may be retrying a failed payment.
            var alreadyPaid = await _repo.Query()
                .AnyAsync(r => r.EventKey == eventKey && r.Phone == phone && r.PaymentStatus == "Paid", ct);
            if (alreadyPaid)
                throw new InvalidOperationException("This phone number is already registered and paid for this event.");

            var amount = ev.Price;
            var currency = ev.Currency;

            // A pending registration on the same phone for the same event is
            // REUSED (the buyer is retrying / changed payment method) instead
            // of piling up a new row per attempt.
            // Reuse only when it is clearly the same person (same name, or the
            // same signed-in account) — a stranger typing someone else's phone
            // must not take over their pending ticket.
            var firstLower = dto.FirstName.Trim().ToLowerInvariant(); var lastLower = dto.LastName.Trim().ToLowerInvariant();
            var callerId = dto.UserId;
            var entity = await _repo.Query(asNoTracking: false)
                .Where(r => r.EventKey == eventKey && r.Phone == phone && r.PaymentStatus == "Pending")
                .Where(r => (callerId != null && r.UserId == callerId)
                         || (r.FirstName.ToLower() == firstLower && r.LastName.ToLower() == lastLower))
                .OrderByDescending(r => r.Id)
                .FirstOrDefaultAsync(ct);
            if (entity is not null && entity.ProviderRef is { Length: > 3 } oldRef && oldRef.StartsWith("OP:", StringComparison.Ordinal) && method != "Visa")
            {
                // Switching away from a card link: close it so it can't be paid later.
                try { await _onlinePayments.CancelOpenByCodeAsync(oldRef[3..], "website", "Customer chose another payment method", ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not cancel old pay link {Ref}", oldRef); }
                entity.ProviderRef = null;
            }
            if (entity is null)
            {
                entity = new EventRegistration
                {
                    EventKey = eventKey,
                    EventId = ev.Id,
                    TicketCode = await NewTicketCodeAsync(ct),
                    CreatedOn = DateTime.UtcNow,
                };
                await _repo.AddAsync(entity, ct);
            }
            entity.FirstName = dto.FirstName.Trim();
            entity.LastName = dto.LastName.Trim();
            entity.Phone = phone;
            entity.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();
            entity.PaymentMethod = method;
            entity.PaymentStatus = "Pending";
            entity.Amount = amount;
            entity.Currency = currency;
            entity.UserId ??= dto.UserId;
            entity.EventId ??= ev.Id;
            entity.TicketCode ??= await NewTicketCodeAsync(ct);
            entity.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);

            var fullName = $"{entity.FirstName} {entity.LastName}";
            var whatsAppUrl = BuildWhatsAppUrl(entity, ev);
            var ticketUrl = await TicketUrlAsync(entity.TicketCode!, ct);

            // Cash → nothing to charge online; admin confirms at the store.
            if (method == "Cash")
            {
                return new EventRegisterResultDto(
                    entity.Id, method, entity.PaymentStatus, amount, currency,
                    RedirectUrl: null,
                    WhatsAppUrl: whatsAppUrl,
                    Message: "You're registered. Pay in cash at the AXIS store before the event — confirm on WhatsApp so we hold your spot.",
                    TicketCode: entity.TicketCode, TicketUrl: ticketUrl);
            }

            // Whish without merchant-API credentials → fall back to the plain
            // payment link the admin pasted. Same manual flow as Cash: the
            // buyer pays through the link, confirms on WhatsApp, and an admin
            // marks it Paid. Only used when the Collect API isn't configured;
            // with credentials we always prefer the auto-confirming path.
            if (method == "Whish" && !await _whish.IsConfiguredAsync(ct))
            {
                if (string.IsNullOrWhiteSpace(ev.WhishPaymentLink))
                    throw new InvalidOperationException("Whish payment is not configured for this event.");

                return new EventRegisterResultDto(
                    entity.Id, method, entity.PaymentStatus, amount, currency,
                    RedirectUrl: null,
                    WhatsAppUrl: whatsAppUrl,
                    Message: $"You're registered. Pay {amount:0.##} {currency} through the Whish link below, then send us the confirmation on WhatsApp so we hold your spot.",
                    PayLinkUrl: ev.WhishPaymentLink,
                    TicketCode: entity.TicketCode, TicketUrl: ticketUrl);
            }

            // Cards → MontyPay through the Online Payments module when it is
            // configured. The registration gets a pay link; the callback marks
            // it Paid (ProviderRef "OP:{code}") and posts the ledger entry.
            if (method == "Visa" && await _onlinePayments.IsProviderReadyAsync(ct))
            {
                try
                {
                    // Reuse a still-open pay link from a previous attempt.
                    string? payUrl = null;
                    if (entity.ProviderRef is { Length: > 3 } pr && pr.StartsWith("OP:", StringComparison.Ordinal))
                    {
                        var open = await _onlinePayments.GetPublicAsync(pr[3..], ct);
                        if (open is { CanPay: true })
                        {
                            var publicBaseReuse = (await _settings.GetRawAsync("Payments.PublicBaseUrl", ct)
                                ?? await _settings.GetRawAsync("Event.PublicBaseUrl", ct) ?? "https://www.axislb.com").TrimEnd('/');
                            payUrl = _onlinePayments.BuildPayUrl(open.Code, publicBaseReuse);
                        }
                    }
                    if (payUrl is null)
                    {
                        if (entity.ProviderRef is { Length: > 3 } stale && stale.StartsWith("OP:", StringComparison.Ordinal))
                        {
                            try { await _onlinePayments.CancelOpenByCodeAsync(stale[3..], "website", "Replaced by a new pay link", ct); }
                            catch (Exception ex) { _logger.LogWarning(ex, "Could not cancel stale pay link {Ref}", stale); }
                        }
                        var (payment, url) = await _onlinePayments.CreateForReferenceAsync(
                            purpose: "EventTicket", referenceType: "EventRegistration", referenceId: entity.Id,
                            amount: amount, currency: currency,
                            description: $"{ev.Title} - Entry Ticket #{entity.Id}",
                            customerName: fullName, customerPhone: entity.Phone, customerEmail: entity.Email,
                            userId: dto.UserId, actor: "website", ct: ct);
                        entity.ProviderRef = $"OP:{payment.Code}";
                        entity.ModifiedOn = DateTime.UtcNow;
                        await _uow.SaveChangesAsync(ct);
                        payUrl = url;
                    }

                    return new EventRegisterResultDto(
                        entity.Id, method, entity.PaymentStatus, amount, currency,
                        RedirectUrl: payUrl,
                        WhatsAppUrl: whatsAppUrl,
                        Message: "Redirecting you to secure card payment…",
                        TicketCode: entity.TicketCode, TicketUrl: ticketUrl);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MontyPay link creation failed for registration {Id} — falling back", entity.Id);
                    if (!await _stripe.IsConfiguredAsync(ct))
                    {
                        entity.AdminNotes = $"Online payment start failed: {ex.Message}";
                        entity.ModifiedOn = DateTime.UtcNow;
                        await _uow.SaveChangesAsync(ct);
                        return new EventRegisterResultDto(
                            entity.Id, method, entity.PaymentStatus, amount, currency,
                            RedirectUrl: null, WhatsAppUrl: whatsAppUrl,
                            Message: "You're registered, but online payment is unavailable right now. Confirm on WhatsApp and we'll arrange payment.",
                            TicketCode: entity.TicketCode, TicketUrl: ticketUrl);
                    }
                }
            }

            // Card (Stripe) / Whish → start the hosted checkout. Success lands
            // straight on the ticket page.
            var publicBase = (await _settings.GetRawAsync("Event.PublicBaseUrl", ct))?.TrimEnd('/')
                             ?? "https://www.axislb.com";
            var successUrl = $"{publicBase}/tickets/{entity.TicketCode}?paid=1";
            var cancelUrl = $"{publicBase}/events/{eventKey}?cancelled=1&reg={entity.Id}";

            IPaymentGateway gateway = method == "Visa" ? _stripe : _whish;
            var start = await gateway.StartAsync(
                entity.Id, amount, currency,
                description: $"{ev.Title} — Entry Ticket",
                customerName: fullName, customerEmail: entity.Email,
                successUrl: successUrl, cancelUrl: cancelUrl, ct: ct);

            if (!start.Success)
            {
                // Don't lose the sign-up just because the gateway hiccuped —
                // keep the row Pending and let them settle in cash instead.
                entity.AdminNotes = $"Gateway start failed: {start.Error}";
                entity.ModifiedOn = DateTime.UtcNow;
                await _uow.SaveChangesAsync(ct);

                return new EventRegisterResultDto(
                    entity.Id, method, entity.PaymentStatus, amount, currency,
                    RedirectUrl: null,
                    WhatsAppUrl: whatsAppUrl,
                    Message: "You're registered, but online payment is unavailable right now. Confirm on WhatsApp and we'll arrange payment.",
                    TicketCode: entity.TicketCode, TicketUrl: ticketUrl);
            }

            entity.ProviderRef = start.Reference;
            entity.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);

            return new EventRegisterResultDto(
                entity.Id, method, entity.PaymentStatus, amount, currency,
                RedirectUrl: start.RedirectUrl,
                WhatsAppUrl: whatsAppUrl,
                Message: "Redirecting you to secure payment…",
                TicketCode: entity.TicketCode, TicketUrl: ticketUrl);
        }

        // ── Tickets ──────────────────────────────────────────────────────
        private async Task<string> NewTicketCodeAsync(CancellationToken ct)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                var code = Events.TicketCodes.NewRandom();
                if (!await _repo.Query().AnyAsync(r => r.TicketCode == code, ct)) return code;
            }
            throw new InvalidOperationException("Could not allocate a ticket code.");
        }

        private async Task<string> TicketUrlAsync(string code, CancellationToken ct)
        {
            var publicBase = ((await _settings.GetRawAsync("Payments.PublicBaseUrl", ct))
                              ?? (await _settings.GetRawAsync("Event.PublicBaseUrl", ct))
                              ?? "https://www.axislb.com").TrimEnd('/');
            return $"{publicBase}/tickets/{code}";
        }

        private static string NormaliseCode(string raw) => Events.TicketCodes.Normalise(raw);

        private async Task<EventTicketDto> ToTicketAsync(EventRegistration r, Event? ev, CancellationToken ct)
        {
            ev ??= await _eventRepo.Query().FirstOrDefaultAsync(e => e.Id == r.EventId || e.Key == r.EventKey, ct);
            string? payUrl = null;
            if (r.PaymentStatus == "Pending" && r.PaymentMethod == "Visa" && r.ProviderRef is { Length: > 3 } pr && pr.StartsWith("OP:", StringComparison.Ordinal))
            {
                try
                {
                    var p = await _onlinePayments.GetPublicAsync(pr[3..], ct);
                    if (p is { CanPay: true })
                    {
                        var publicBase = ((await _settings.GetRawAsync("Payments.PublicBaseUrl", ct))
                                          ?? (await _settings.GetRawAsync("Event.PublicBaseUrl", ct)) ?? "https://www.axislb.com").TrimEnd('/');
                        payUrl = _onlinePayments.BuildPayUrl(p.Code, publicBase);
                    }
                }
                catch { /* pay link is a convenience */ }
            }
            if (payUrl is null && r.PaymentStatus == "Pending" && r.PaymentMethod == "Whish" && !string.IsNullOrWhiteSpace(ev?.WhishPaymentLink))
                payUrl = ev!.WhishPaymentLink;

            return new EventTicketDto(
                r.TicketCode ?? "", r.Id, r.EventKey,
                ev?.Title ?? r.EventKey, ev?.Subtitle, ev?.EventDate, ev?.Location, ev?.HeroImagePath,
                r.FirstName, r.LastName, r.Phone, r.Email,
                r.PaymentMethod, r.PaymentStatus, r.Amount, r.Currency,
                r.PaymentStatus == "Paid" ? r.ConfirmedOn : null,
                r.CheckedInOn, r.CheckedInBy, r.CreatedOn,
                payUrl,
                ev is null ? null : BuildWhatsAppUrl(r, ev),
                ev?.EventDate is null || ev.EventDate.Value >= DateTime.UtcNow.AddHours(-12));
        }

        public async Task<EventTicketDto?> GetTicketAsync(string ticketCode, CancellationToken ct = default)
        {
            var code = NormaliseCode(ticketCode);
            if (code.Length < 6) return null;
            var r = await _repo.Query().FirstOrDefaultAsync(x => x.TicketCode == code, ct);
            return r is null ? null : await ToTicketAsync(r, null, ct);
        }

        public async Task<string?> TicketCodeForRegistrationAsync(int registrationId, CancellationToken ct = default)
            => await _repo.Query().Where(r => r.Id == registrationId).Select(r => r.TicketCode).FirstOrDefaultAsync(ct);

        public async Task<List<EventTicketDto>> MyTicketsAsync(int userId, string? phone, CancellationToken ct = default)
        {
            var p = string.IsNullOrWhiteSpace(phone) ? null : NormalisePhone(phone);
            var rows = await _repo.Query()
                .Where(r => r.UserId == userId || (p != null && r.Phone == p))
                .Where(r => r.PaymentStatus != "Rejected")
                .OrderByDescending(r => r.Id).Take(50).ToListAsync(ct);
            var evIds = rows.Select(r => r.EventId).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
            var keys = rows.Select(r => r.EventKey).Distinct().ToList();
            var events = await _eventRepo.Query().Where(e => evIds.Contains(e.Id) || keys.Contains(e.Key)).ToListAsync(ct);
            var list = new List<EventTicketDto>();
            foreach (var r in rows)
                list.Add(await ToTicketAsync(r, events.FirstOrDefault(e => e.Id == r.EventId) ?? events.FirstOrDefault(e => e.Key == r.EventKey), ct));
            return list;
        }

        public async Task<TicketCheckInResultDto> CheckInAsync(string ticketCode, string actor, string? eventKey, CancellationToken ct = default)
        {
            var code = NormaliseCode(ticketCode);
            var r = code.Length >= 6 ? await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.TicketCode == code, ct) : null;
            if (r is null) return new(false, "not_found", "Ticket not found — check the code and try again.", null);
            var ev = await _eventRepo.Query().FirstOrDefaultAsync(e => e.Id == r.EventId || e.Key == r.EventKey, ct);
            var ticket = await ToTicketAsync(r, ev, ct);

            if (!string.IsNullOrWhiteSpace(eventKey) && !string.Equals(eventKey.Trim(), r.EventKey, StringComparison.OrdinalIgnoreCase))
                return new(false, "wrong_event", $"This ticket is for \"{ticket.EventTitle}\", not this event.", ticket);
            if (r.PaymentStatus == "Rejected" || r.PaymentStatus == "Refunded")
                return new(false, "rejected", $"Ticket is {r.PaymentStatus.ToLowerInvariant()} — not valid for entry.", ticket);
            if (r.PaymentStatus != "Paid")
                return new(false, "unpaid", $"Not paid yet ({r.PaymentMethod}). Take {r.Amount:0.##} {r.Currency} and press \"Confirm cash\" to admit.", ticket);
            if (r.CheckedInOn.HasValue)
            {
                var minutesAgo = (int)(DateTime.UtcNow - r.CheckedInOn.Value).TotalMinutes;
                return new(false, "already", $"Already checked in by {r.CheckedInBy} ({minutesAgo} min ago).", ticket);
            }

            r.CheckedInOn = DateTime.UtcNow; r.CheckedInBy = actor; r.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation("Ticket {Code} checked in by {Actor}", code, actor);
            return new(true, "ok", $"Welcome {r.FirstName} {r.LastName} — checked in.", await ToTicketAsync(r, ev, ct));
        }

        public async Task<bool> UndoCheckInAsync(int registrationId, string actor, CancellationToken ct = default)
        {
            var r = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == registrationId, ct);
            if (r is null) return false;
            r.CheckedInOn = null; r.CheckedInBy = null; r.ModifiedOn = DateTime.UtcNow;
            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation("Check-in undone for registration {Id} by {Actor}", registrationId, actor);
            return true;
        }

        public async Task<EventAttendeeListDto?> AttendeesAsync(string eventKey, string? search, CancellationToken ct = default)
        {
            var ev = await _eventRepo.Query().FirstOrDefaultAsync(e => e.Key == eventKey, ct);
            if (ev is null) return null;
            var q = _repo.Query().Where(r => r.EventKey == eventKey && r.PaymentStatus != "Rejected");
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim().ToLower();
                q = q.Where(r => r.FirstName.ToLower().Contains(s) || r.LastName.ToLower().Contains(s) || r.Phone.Contains(s)
                              || (r.TicketCode != null && r.TicketCode.ToLower().Contains(s)));
            }
            var rows = await q.OrderBy(r => r.CheckedInOn == null ? 0 : 1).ThenBy(r => r.LastName).ThenBy(r => r.FirstName).ToListAsync(ct);
            var all = await _repo.Query().Where(r => r.EventKey == eventKey).Select(r => new { r.PaymentStatus, r.CheckedInOn }).ToListAsync(ct);
            return new EventAttendeeListDto(
                ev.Key, ev.Title, ev.EventDate, ev.Capacity,
                all.Count(a => a.PaymentStatus == "Paid"), all.Count(a => a.PaymentStatus == "Pending"), all.Count(a => a.CheckedInOn != null),
                rows.Select(r => new EventAttendeeDto(r.Id, r.TicketCode ?? "", r.FirstName, r.LastName, r.Phone, r.Email,
                    r.PaymentMethod, r.PaymentStatus, r.Amount, r.Currency, r.CheckedInOn, r.CheckedInBy, r.CreatedOn)).ToList());
        }

        public async Task<bool> ConfirmCashAtTillAsync(int registrationId, string actor, CancellationToken ct = default)
        {
            var r = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(x => x.Id == registrationId, ct);
            if (r is null) return false;
            if (r.PaymentStatus == "Paid") return true;
            r.PaymentMethod = "Cash";        // whatever they chose online, the money arrived as cash
            return await SetStatusAsync(registrationId, "Paid", $"till:{actor}", "Paid in cash at the till", ct);
        }

        // ── Admin ────────────────────────────────────────────────────────
        public async Task<PaginatedResponse<EventRegistrationDto>> ListAsync(EventRegistrationFilterDto filter, CancellationToken ct = default)
        {
            var q = _repo.Query();

            if (!string.IsNullOrWhiteSpace(filter.EventKey))
                q = q.Where(r => r.EventKey == filter.EventKey);
            if (!string.IsNullOrWhiteSpace(filter.PaymentStatus))
                q = q.Where(r => r.PaymentStatus == filter.PaymentStatus);
            if (!string.IsNullOrWhiteSpace(filter.PaymentMethod))
                q = q.Where(r => r.PaymentMethod == filter.PaymentMethod);
            if (!string.IsNullOrWhiteSpace(filter.Search))
            {
                var s = filter.Search.Trim().ToLower();
                q = q.Where(r => r.FirstName.ToLower().Contains(s)
                              || r.LastName.ToLower().Contains(s)
                              || r.Phone.Contains(s)
                              || (r.Email != null && r.Email.ToLower().Contains(s)));
            }

            var total = await q.CountAsync(ct);
            var page = Math.Max(1, filter.Page);
            var size = Math.Clamp(filter.PageSize, 1, 500);

            var rows = await q
                .OrderByDescending(r => r.CreatedOn).ThenByDescending(r => r.Id)
                .Skip((page - 1) * size).Take(size)
                .Select(r => new EventRegistrationDto(
                    r.Id, r.EventKey, r.FirstName, r.LastName, r.Phone, r.Email,
                    r.PaymentMethod, r.PaymentStatus, r.Amount, r.Currency,
                    r.ProviderRef, r.ConfirmedBy, r.ConfirmedOn, r.AdminNotes, r.CreatedOn, r.TicketCode, r.CheckedInOn))
                .ToListAsync(ct);

            return new PaginatedResponse<EventRegistrationDto>(total, rows, page, size);
        }

        public async Task<EventRegistrationStatsDto> GetStatsAsync(string? eventKey, CancellationToken ct = default)
        {
            var q = _repo.Query();
            if (!string.IsNullOrWhiteSpace(eventKey)) q = q.Where(r => r.EventKey == eventKey);

            var rows = await q.Select(r => new { r.PaymentStatus, r.Amount }).ToListAsync(ct);

            return new EventRegistrationStatsDto(
                Total: rows.Count,
                Paid: rows.Count(r => r.PaymentStatus == "Paid"),
                Pending: rows.Count(r => r.PaymentStatus == "Pending"),
                Rejected: rows.Count(r => r.PaymentStatus == "Rejected"),
                CollectedAmount: Math.Round(rows.Where(r => r.PaymentStatus == "Paid").Sum(r => r.Amount), 2),
                PendingAmount: Math.Round(rows.Where(r => r.PaymentStatus == "Pending").Sum(r => r.Amount), 2));
        }

        /// <summary>
        /// Posts journal entries for registrations that were already Paid
        /// before ledger posting existed (or whose posting failed at the
        /// time). Safe to run repeatedly — each registration is guarded by
        /// the journal service's idempotency check, so ones already on the
        /// books are skipped.
        /// </summary>
        public async Task<EventLedgerBackfillResultDto> BackfillLedgerAsync(
            string? eventKey, bool dryRun, CancellationToken ct = default)
        {
            var q = _repo.Query().Where(r => r.PaymentStatus == "Paid" && r.Amount > 0);
            if (!string.IsNullOrWhiteSpace(eventKey))
                q = q.Where(r => r.EventKey == eventKey);

            var rows = await q
                .OrderBy(r => r.Id)
                .Select(r => new { r.Id, r.Amount })
                .ToListAsync(ct);

            var errors = new List<string>();
            int posted = 0, skipped = 0;
            decimal amountPosted = 0m;

            foreach (var r in rows)
            {
                if (dryRun)
                {
                    // Report what WOULD happen without touching the books.
                    // Mirror the live guard exactly: only a posted, non-voided
                    // entry counts as "already booked".
                    var already = await _journal
                        .GetJournalEntriesByReferenceAsync(JournalService.EventReferenceType, r.Id, ct);
                    var hasLive = already.Data?.Any(e => e.IsPosted && !e.IsVoided) == true;
                    if (hasLive) skipped++;
                    else { posted++; amountPosted += r.Amount; }
                    continue;
                }

                try
                {
                    var res = await _journal.CreateJournalEntryFromEventRegistrationAsync(r.Id, ct);
                    if (res.Success) { posted++; amountPosted += r.Amount; }
                    else if ((res.Error ?? "").Contains("already exists", StringComparison.OrdinalIgnoreCase))
                        skipped++;
                    else
                    {
                        skipped++;
                        errors.Add($"#{r.Id}: {res.Error}");
                    }
                }
                catch (Exception ex)
                {
                    skipped++;
                    errors.Add($"#{r.Id}: {ex.Message}");

                    // The whole loop shares one DbContext. Without this, the
                    // failed row's pending changes stay in the tracker and get
                    // re-applied on the next row's save, turning one bad
                    // registration into a batch of identical errors.
                    _uow.ResetChangeTracker();
                }
            }

            _logger.LogInformation(
                "Event ledger backfill ({Mode}): {Posted} posted, {Skipped} skipped, {Errors} error(s)",
                dryRun ? "dry run" : "live", posted, skipped, errors.Count);

            return new EventLedgerBackfillResultDto(
                rows.Count, posted, skipped, Math.Round(amountPosted, 2), dryRun,
                errors.Take(100).ToList());
        }

        public async Task<bool> ConfirmPaymentAsync(int id, string actor, string? notes, CancellationToken ct = default)
            => await SetStatusAsync(id, "Paid", actor, notes, ct);

        public async Task<bool> RejectPaymentAsync(int id, string actor, string? notes, CancellationToken ct = default)
            => await SetStatusAsync(id, "Rejected", actor, notes, ct);

        private async Task<bool> SetStatusAsync(int id, string status, string actor, string? notes, CancellationToken ct)
        {
            var e = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(r => r.Id == id, ct);
            if (e is null) return false;

            var wasPaid = e.PaymentStatus == "Paid";

            e.PaymentStatus = status;
            e.ConfirmedBy = actor;
            e.ConfirmedOn = DateTime.UtcNow;
            e.ModifiedOn = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(notes)) e.AdminNotes = notes.Trim();

            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation("Event registration {Id} marked {Status} by {Actor}", id, status, actor);

            // Paid by other means (cash at the till / admin) or rejected: an
            // open card link must not stay payable — that would be a double charge.
            if (status != "Pending" && e.ProviderRef is { Length: > 3 } op && op.StartsWith("OP:", StringComparison.Ordinal))
            {
                try { await _onlinePayments.CancelOpenByCodeAsync(op[3..], actor, $"Registration marked {status} by {actor}", ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not cancel pay link {Ref} for registration {Id}", op, id); }
            }

            // Accounting runs AFTER the status is committed, and never blocks
            // it — same contract as sales postings. If the ledger write fails
            // the admin's action still stands and the gap shows up in the
            // books audit rather than as a failed click.
            if (status == "Paid")
                await PostToLedgerAsync(id, ct);
            else if (wasPaid)
                await ReverseLedgerAsync(id, $"Registration {status.ToLowerInvariant()} by {actor}", ct);

            return true;
        }

        /// <summary>Writes DR 1000 Cash / CR 4300 Event Revenue. Never throws.</summary>
        private async Task PostToLedgerAsync(int registrationId, CancellationToken ct)
        {
            try
            {
                var res = await _journal.CreateJournalEntryFromEventRegistrationAsync(registrationId, ct);
                if (res.Success)
                    _logger.LogInformation(
                        "Journal entry {EntryNumber} posted for event registration {Id}",
                        res.Data?.EntryNumber, registrationId);
                else
                    // "already exists" lands here too — that's the idempotency
                    // guard doing its job on a repeated confirm, not a fault.
                    _logger.LogWarning(
                        "No journal entry posted for event registration {Id}: {Error}",
                        registrationId, res.Error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception posting journal entry for event registration {Id}", registrationId);
            }
        }

        /// <summary>Reverses a prior posting. Never throws.</summary>
        private async Task ReverseLedgerAsync(int registrationId, string reason, CancellationToken ct)
        {
            try
            {
                var res = await _journal.VoidJournalEntryForEventRegistrationAsync(registrationId, reason, null, ct);
                if (!res.Success)
                    _logger.LogWarning(
                        "Could not reverse journal entry for event registration {Id}: {Error}",
                        registrationId, res.Error);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception reversing journal entry for event registration {Id}", registrationId);
            }
        }

        // ── Gateway callback ─────────────────────────────────────────────
        public async Task<bool> MarkPaidByProviderRefAsync(string providerRef, string? rawPayload, CancellationToken ct = default, int? registrationId = null)
        {
            if (string.IsNullOrWhiteSpace(providerRef) && registrationId is null) return false;

            var e = string.IsNullOrWhiteSpace(providerRef) ? null : await _repo.Query(asNoTracking: false)
                .FirstOrDefaultAsync(r => r.ProviderRef == providerRef, ct);
            if (e is null && registrationId is int rid)
            {
                // The registration may have moved on to another pay link since
                // this one was created; money arrived on THIS link, so the
                // registration id the payment carries is the truth.
                e = await _repo.Query(asNoTracking: false).FirstOrDefaultAsync(r => r.Id == rid, ct);
                if (e is not null && e.PaymentStatus != "Paid")
                    _logger.LogWarning("Payment {Ref} settled on a superseded link — applying to registration {Id} by id", providerRef, rid);
            }
            if (e is null)
            {
                _logger.LogWarning("Payment callback for unknown providerRef {Ref}", providerRef);
                return false;
            }

            // Idempotent — a provider may deliver the same webhook twice.
            if (e.PaymentStatus == "Paid") return true;

            // The money arrived through an Online Payments card link, whatever the
            // registration says now (the buyer may have re-registered as Cash
            // while the old link stayed open). The ledger picks the debit
            // account from PaymentMethod — card money must land on 1050, not 1000.
            if (providerRef?.StartsWith("OP:", StringComparison.Ordinal) == true && e.PaymentMethod is not ("Visa" or "Card" or "MontyPay" or "Online"))
            {
                _logger.LogWarning("Registration {Id} was {Method} but settled by card link {Ref} — recording it as Visa", e.Id, e.PaymentMethod, providerRef);
                e.PaymentMethod = "Visa";
            }
            e.PaymentStatus = "Paid";
            e.ConfirmedBy = "gateway";
            e.ConfirmedOn = DateTime.UtcNow;
            e.ModifiedOn = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(rawPayload))
                e.ProviderPayload = rawPayload.Length > 8000 ? rawPayload[..8000] : rawPayload;

            await _uow.SaveChangesAsync(ct);
            _logger.LogInformation("Registration {Id} auto-confirmed via gateway ref {Ref}", e.Id, providerRef);

            // Same ledger posting as a manual confirm. The idempotency guard
            // inside the journal service means a webhook delivered twice
            // books once.
            await PostToLedgerAsync(e.Id, ct);
            return true;
        }

        // ── Helpers ──────────────────────────────────────────────────────
        /// <summary>
        /// Builds the wa.me deep link the registrant taps to confirm payment.
        /// The number and message body are both per-event, edited by the
        /// admin. The template supports {{placeholders}} — anything the
        /// admin doesn't use is simply left out.
        /// </summary>
        private static string? BuildWhatsAppUrl(EventRegistration e, Event ev)
        {
            var number = ev.WhatsAppNumber;
            if (string.IsNullOrWhiteSpace(number)) return null;

            var template = string.IsNullOrWhiteSpace(ev.WhatsAppTemplate)
                ? DefaultWhatsAppTemplate
                : ev.WhatsAppTemplate;

            var msg = RenderTemplate(template, e, ev);
            var digits = new string(number.Where(char.IsDigit).ToArray());
            return $"https://wa.me/{digits}?text={Uri.EscapeDataString(msg)}";
        }

        private const string DefaultWhatsAppTemplate =
            "{{eventTitle}} — Registration #{{registrationId}}\n\n" +
            "Name: {{fullName}}\n" +
            "Phone: {{phone}}\n" +
            "Payment: {{paymentMethod}}\n" +
            "Amount: {{amount}} {{currency}}\n\n" +
            "I'd like to confirm my payment.";

        /// <summary>
        /// Replaces {{placeholders}} in the admin's template. Unknown tokens
        /// are left untouched so a typo is visible rather than silently
        /// producing an empty line.
        /// </summary>
        private static string RenderTemplate(string template, EventRegistration e, Event ev)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["eventTitle"]      = ev.Title,
                ["eventKey"]        = ev.Key,
                ["eventDate"]       = ev.EventDate?.ToString("dd MMM yyyy, HH:mm") ?? "",
                ["location"]        = ev.Location ?? "",
                ["registrationId"]  = e.Id.ToString(),
                ["firstName"]       = e.FirstName,
                ["lastName"]        = e.LastName,
                ["fullName"]        = $"{e.FirstName} {e.LastName}",
                ["phone"]           = e.Phone,
                ["email"]           = e.Email ?? "",
                ["paymentMethod"]   = e.PaymentMethod,
                ["amount"]          = e.Amount.ToString("0.##"),
                ["currency"]        = e.Currency,
            };

            var result = template;
            foreach (var (k, v) in map)
                result = result.Replace("{{" + k + "}}", v, StringComparison.OrdinalIgnoreCase);
            return result;
        }

        private static string NormalisePhone(string raw)
        {
            var trimmed = (raw ?? "").Trim();
            var kept = new string(trimmed.Where(c => char.IsDigit(c) || c == '+').ToArray());
            return kept;
        }
    }
}
