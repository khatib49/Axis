using Application.DTOs;

namespace Application.IServices
{
    /// <summary>
    /// Phase 3: per-branch stock position and "receive a purchase to a location".
    /// </summary>
    public interface IBranchStockService
    {
        /// <summary>
        /// Stock held by one branch: quantity at its default location, at its other
        /// locations, reorder level, below-level flag and value. branchId null falls
        /// back to the X-Branch-Id header, then to the shared scope (global Main).
        /// </summary>
        Task<BranchStockPositionDto> GetBranchPositionAsync(int? branchId, CancellationToken ct = default);

        /// <summary>All branches side by side (shared scope first). Admin view.</summary>
        Task<StockPositionsDto> GetPositionsAsync(CancellationToken ct = default);

        /// <summary>
        /// Moves the quantities of an existing purchase (its PurchaseLines) from the
        /// global default location to <c>LocationId</c> by creating and posting a
        /// StockTransfer referenced "PO-{purchaseId}". Idempotent per purchase.
        /// </summary>
        Task<BaseResponse<ReceiveToLocationResultDto>> ReceiveToLocationAsync(ReceiveToLocationDto dto, CancellationToken ct = default);
    }
}
