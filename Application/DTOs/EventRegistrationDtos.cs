namespace Application.DTOs
{
    // ── Public (anonymous) submission ────────────────────────────────────
    public record EventRegisterRequestDto(
        string FirstName,
        string LastName,
        string Phone,
        string? Email,
        string PaymentMethod,          // 'Visa' | 'Whish' | 'Cash'
        string EventKey = "squid-game-x-axis",
        /// Website account id when the visitor is signed in (set by the controller, never trusted from the body).
        int? UserId = null,
        /// Chosen ticket type (required when the event has ticket types). The price always comes from the server.
        string? TicketTypeKey = null
    );

    /// <summary>
    /// What the landing page gets back. RedirectUrl is non-null for Visa
    /// (Stripe Checkout) and Whish (Collect page) — the browser should send
    /// the user there. For Cash it stays null and the page shows the
    /// WhatsApp confirmation button instead.
    /// </summary>
    public record EventRegisterResultDto(
        int RegistrationId,
        string PaymentMethod,
        string PaymentStatus,
        decimal Amount,
        string Currency,
        string? RedirectUrl,
        string? WhatsAppUrl,
        string Message,
        /// <summary>
        /// Manual Whish link: the buyer opens it, pays, then confirms on
        /// WhatsApp. Set only when the Collect API isn't configured, so the
        /// page shows a "Pay with Whish" button instead of auto-redirecting.
        /// </summary>
        string? PayLinkUrl = null,
        /// The ticket code — the ticket page is /tickets/{TicketCode} (Pending until paid).
        string? TicketCode = null,
        string? TicketUrl = null,
        string? TicketTypeName = null
    );

    // ── Ticket (public page, my tickets, door check-in) ──────────────────
    public record EventTicketDto(
        string TicketCode,
        int RegistrationId,
        string EventKey,
        string EventTitle,
        string? EventSubtitle,
        DateTime? EventDate,
        string? Location,
        string? HeroImagePath,
        string FirstName,
        string LastName,
        string Phone,
        string? Email,
        string PaymentMethod,
        /// Pending | Paid | Rejected | Refunded
        string PaymentStatus,
        decimal Amount,
        string Currency,
        DateTime? PaidOn,
        DateTime? CheckedInOn,
        string? CheckedInBy,
        DateTime CreatedOn,
        /// Open pay link when the ticket is Pending and the card payment is still valid.
        string? PayUrl,
        string? WhatsAppUrl,
        /// True when the event has not started yet (ticket is usable).
        bool IsUpcoming,
        string? TicketTypeName = null
    );

    /// <summary>Door scan result.</summary>
    public record TicketCheckInResultDto(
        bool Ok,
        /// ok | already | unpaid | not_found | wrong_event | rejected
        string Outcome,
        string Message,
        EventTicketDto? Ticket
    );

    /// <summary>Attendee row for the cashier events board.</summary>
    public record EventAttendeeDto(
        int Id, string TicketCode, string FirstName, string LastName, string Phone, string? Email,
        string PaymentMethod, string PaymentStatus, decimal Amount, string Currency,
        DateTime? CheckedInOn, string? CheckedInBy, DateTime CreatedOn,
        string? TicketTypeName = null);

    public record EventAttendeeListDto(
        string EventKey, string EventTitle, DateTime? EventDate, int? Capacity,
        int Paid, int Pending, int CheckedIn, List<EventAttendeeDto> Attendees);

    // ── Admin panel ──────────────────────────────────────────────────────
    public record EventRegistrationDto(
        int Id,
        string EventKey,
        string FirstName,
        string LastName,
        string Phone,
        string? Email,
        string PaymentMethod,
        string PaymentStatus,
        decimal Amount,
        string Currency,
        string? ProviderRef,
        string? ConfirmedBy,
        DateTime? ConfirmedOn,
        string? AdminNotes,
        DateTime CreatedOn,
        string? TicketCode = null,
        DateTime? CheckedInOn = null,
        string? TicketTypeName = null
    );

    public record EventRegistrationFilterDto(
        string? EventKey = null,
        string? PaymentStatus = null,
        string? PaymentMethod = null,
        string? Search = null,
        int Page = 1,
        int PageSize = 50,
        /// Ticket type name (as shown in the list) to filter on.
        string? TicketType = null
    );

    public record EventRegistrationStatsDto(
        int Total,
        int Paid,
        int Pending,
        int Rejected,
        decimal CollectedAmount,
        decimal PendingAmount
    );

    public record ConfirmPaymentRequestDto(string? Notes);

    /// <summary>Outcome of POST /api/admin/event-registrations/backfill-ledger.</summary>
    public record EventLedgerBackfillResultDto(
        int Examined,
        int Posted,
        int Skipped,
        decimal AmountPosted,
        bool DryRun,
        List<string> Errors
    );

    // ── Public event config for the landing page ─────────────────────────
    public record EventPublicConfigDto(
        string EventKey,
        decimal Price,
        string Currency,
        string? WhatsAppNumber,
        bool StripeEnabled,
        bool WhishEnabled
    );
}
