using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Promotions
{
    /// <summary>
    /// The single guarded write to transactions.TotalPrice from this module.
    /// See <see cref="IPromotionInvoiceAdjuster"/> for the contract.
    ///
    /// Idempotency: every adjustment writes a TransactionAuditLog row with
    /// Action = "PromotionAdjust" and Notes starting with "[key]"; a second
    /// call with the same key on the same invoice is refused (AlreadyApplied)
    /// whether the first one is already saved or still pending in the same
    /// change tracker.
    /// </summary>
    public class PromotionInvoiceAdjuster : IPromotionInvoiceAdjuster
    {
        public const string AuditAction = "PromotionAdjust";
        public const int OpenInvoiceStatusId = 7;

        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<PromotionInvoiceAdjuster> _logger;

        public PromotionInvoiceAdjuster(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<PromotionInvoiceAdjuster> logger)
        {
            _db = db;
            _user = user;
            _logger = logger;
        }

        public static string KeyPrefix(string idempotencyKey) => $"[{idempotencyKey}]";

        public async Task<InvoiceAdjustResultDto> ApplyAsync(
            int transactionId, decimal delta, string idempotencyKey, string note, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return new InvoiceAdjustResultDto(false, "An idempotency key is required.", transactionId, 0, 0, 0);

            delta = Math.Round(delta, 2);
            if (delta == 0m)
                return new InvoiceAdjustResultDto(false, "Nothing to apply (delta is 0).", transactionId, 0, 0, 0);

            var trx = await _db.Set<TransactionRecord>()
                .FirstOrDefaultAsync(t => t.Id == transactionId, ct);

            if (trx is null)
                return new InvoiceAdjustResultDto(false, "The specified invoice does not exist.", transactionId, 0, 0, 0);

            if (trx.StatusId != OpenInvoiceStatusId)
                return new InvoiceAdjustResultDto(false,
                    "Promotions can only be applied to an open invoice.", transactionId, trx.TotalPrice, trx.TotalPrice, 0);

            var prefix = KeyPrefix(idempotencyKey);

            var alreadySaved = await _db.Set<TransactionAuditLog>()
                .AnyAsync(a => a.TransactionId == transactionId && a.Action == AuditAction
                               && a.Notes != null && a.Notes.StartsWith(prefix), ct);
            var alreadyPending = _db.ChangeTracker.Entries<TransactionAuditLog>()
                .Any(e => e.State == EntityState.Added && e.Entity.TransactionId == transactionId
                          && e.Entity.Action == AuditAction && (e.Entity.Notes ?? "").StartsWith(prefix));
            if (alreadySaved || alreadyPending)
                return new InvoiceAdjustResultDto(false, "This promotion was already applied to the invoice.",
                    transactionId, trx.TotalPrice, trx.TotalPrice, 0, AlreadyApplied: true);

            var oldTotal = trx.TotalPrice;
            var newTotal = Math.Round(oldTotal + delta, 2);
            if (newTotal < 0m)
                return new InvoiceAdjustResultDto(false,
                    $"Adjustment of {delta:F2} would take the invoice total ({oldTotal:F2}) below zero.",
                    transactionId, oldTotal, oldTotal, 0);

            trx.TotalPrice = newTotal;
            trx.ModifiedOn = DateTime.UtcNow;

            _db.Set<TransactionAuditLog>().Add(new TransactionAuditLog
            {
                TransactionId = transactionId,
                ChangedBy = _user.UserName ?? "system",
                ChangedOn = DateTime.UtcNow,
                Action = AuditAction,
                FieldChanged = "TotalPrice",
                OldValue = oldTotal.ToString("F2"),
                NewValue = newTotal.ToString("F2"),
                Notes = $"{prefix} {note}".Trim(),
            });

            _logger.LogInformation("Promotion {Key} adjusted invoice {TxId} by {Delta}: {Old} -> {New}",
                idempotencyKey, transactionId, delta, oldTotal, newTotal);

            return new InvoiceAdjustResultDto(true, null, transactionId, oldTotal, newTotal, delta);
        }
    }
}
