using Application.DTOs;

namespace Application.IServices
{
    /// <summary>
    /// Hook called from order creation paths. Given a set of items being
    /// sold (or returned), deducts (or restores) ingredient stock based on
    /// each item's recipe, writes StockMovement rows, and returns warnings
    /// for any ingredient whose post-balance went negative.
    ///
    /// Items without a recipe are silently skipped (per the rollout
    /// decision — sales continue normally while recipes are filled in).
    /// </summary>
    public interface IStockService
    {
        /// <summary>
        /// Deduct stock for an order's items. Call inside the same DB
        /// transaction as the order save so it's all-or-nothing.
        /// </summary>
        /// <param name="transactionId">Source TransactionRecord.Id — written into StockMovement.ReferenceId for audit.</param>
        /// <param name="lines">(itemId, quantity sold) pairs.</param>
        /// <param name="actor">User name for audit.</param>
        Task<IReadOnlyList<StockConsumptionWarningDto>> ConsumeForOrderAsync(
            int transactionId,
            IReadOnlyList<(int itemId, decimal quantity)> lines,
            string? actor,
            CancellationToken ct = default);

        /// <summary>
        /// Mirror reversal — when a transaction is voided we restore the
        /// stock and write opposite-signed StockMovements so the audit
        /// trail shows both halves.
        /// </summary>
        Task RestoreForOrderAsync(
            int transactionId,
            string? actor,
            CancellationToken ct = default);
    }
}
