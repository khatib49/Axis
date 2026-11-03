using Application.DTOs;

namespace Application.IServices
{
    /// <summary>
    /// The ONE permitted touch on an existing table from the Promotions
    /// module: nudge transactions.TotalPrice of an OPEN invoice (StatusId 7)
    /// by a signed delta and write a TransactionAuditLog row.
    ///
    /// Guards: invoice must exist and be open; the new total can never go
    /// below 0; the same idempotency key can only be applied once per
    /// invoice. Does NOT call SaveChanges — the caller batches it with its
    /// own rows so the whole promotion commits or nothing does.
    /// </summary>
    public interface IPromotionInvoiceAdjuster
    {
        Task<InvoiceAdjustResultDto> ApplyAsync(
            int transactionId, decimal delta, string idempotencyKey, string note, CancellationToken ct = default);
    }
}
