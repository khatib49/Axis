namespace Application.DTOs
{
    // ── Public (anonymous) submission ────────────────────────────────────
    public record EventRegisterRequestDto(
        string FirstName,
        string LastName,
        string Phone,
        string? Email,
        string PaymentMethod,          // 'Visa' | 'Whish' | 'Cash'
        string EventKey = "squid-game-x-axis"
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
        string? PayLinkUrl = null
    );

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
        DateTime CreatedOn
    );

    public record EventRegistrationFilterDto(
        string? EventKey = null,
        string? PaymentStatus = null,
        string? PaymentMethod = null,
        string? Search = null,
        int Page = 1,
        int PageSize = 50
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
