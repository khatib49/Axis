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

        // ── Gateway callbacks ────────────────────────────────────────────
        /// <summary>Marks a registration paid by provider reference (Stripe session id / Whish externalId). Idempotent.</summary>
        Task<bool> MarkPaidByProviderRefAsync(string providerRef, string? rawPayload, CancellationToken ct = default);
    }
}
