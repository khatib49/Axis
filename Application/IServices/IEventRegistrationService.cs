using Application.DTOs;

namespace Application.IServices
{
    public interface IEventRegistrationService
    {
        /// <summary>Public config the anonymous landing page needs (price, WhatsApp number, which gateways are live).</summary>
        Task<EventPublicConfigDto> GetPublicConfigAsync(string eventKey, CancellationToken ct = default);

        /// <summary>
        /// Creates the registration row, then — depending on the chosen
        /// method — starts a Stripe Checkout session or a Whish Collect
        /// request and returns the redirect URL. Cash registrations come
        /// back with a prefilled WhatsApp link instead.
        /// </summary>
        Task<EventRegisterResultDto> RegisterAsync(EventRegisterRequestDto dto, CancellationToken ct = default);

        // ── Admin ────────────────────────────────────────────────────────
        Task<PaginatedResponse<EventRegistrationDto>> ListAsync(EventRegistrationFilterDto filter, CancellationToken ct = default);
        Task<EventRegistrationStatsDto> GetStatsAsync(string? eventKey, CancellationToken ct = default);
        Task<bool> ConfirmPaymentAsync(int id, string actor, string? notes, CancellationToken ct = default);
        Task<bool> RejectPaymentAsync(int id, string actor, string? notes, CancellationToken ct = default);

        /// <summary>
        /// Posts journal entries for already-Paid registrations that never got
        /// one. Idempotent; pass dryRun to preview.
        /// </summary>
        Task<EventLedgerBackfillResultDto> BackfillLedgerAsync(
            string? eventKey, bool dryRun, CancellationToken ct = default);

        // ── Tickets ──────────────────────────────────────────────────────
        /// <summary>Public ticket by its secret code (null when unknown).</summary>
        Task<EventTicketDto?> GetTicketAsync(string ticketCode, CancellationToken ct = default);
        /// <summary>Tickets that belong to a signed-in website customer (by UserId or the account's phone).</summary>
        Task<List<EventTicketDto>> MyTicketsAsync(int userId, string? phone, CancellationToken ct = default);
        /// <summary>Door scan: marks a PAID ticket as checked in. Idempotent, never throws.</summary>
        Task<TicketCheckInResultDto> CheckInAsync(string ticketCode, string actor, string? eventKey, CancellationToken ct = default);
        /// <summary>Undo a check-in (wrong scan).</summary>
        Task<bool> UndoCheckInAsync(int registrationId, string actor, CancellationToken ct = default);
        /// <summary>Attendee list for one event (cashier board).</summary>
        Task<EventAttendeeListDto?> AttendeesAsync(string eventKey, string? search, CancellationToken ct = default);
        /// <summary>Till: the customer paid cash at the counter/door → Paid + ledger (1000). Same as admin confirm but allowed for cashiers.</summary>
        Task<bool> ConfirmCashAtTillAsync(int registrationId, string actor, CancellationToken ct = default);

        // ── Gateway callbacks ────────────────────────────────────────────
        /// <summary>Marks a registration paid by provider reference (Stripe session id / Whish externalId). Idempotent.</summary>
        Task<bool> MarkPaidByProviderRefAsync(string providerRef, string? rawPayload, CancellationToken ct = default, int? registrationId = null);
        /// <summary>Ticket code for a registration id (used by the pay result page to link to the ticket).</summary>
        Task<string?> TicketCodeForRegistrationAsync(int registrationId, CancellationToken ct = default);
    }
}
