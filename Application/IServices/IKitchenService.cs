using Application.DTOs;

namespace Application.IServices
{
    public interface IKitchenService
    {
        /// <summary>
        /// Get all FNB orders for kitchen (excludes TCG Retail)
        /// Filtered by food status
        /// </summary>
        Task<List<KitchenOrderDto>> GetKitchenOrdersAsync(int? foodStatusId = null, CancellationToken ct = default);

        /// <summary>
        /// Get a specific kitchen order by transaction ID
        /// </summary>
        Task<KitchenOrderDto?> GetKitchenOrderByIdAsync(int transactionId, CancellationToken ct = default);

        /// <summary>
        /// Update food preparation status
        /// Chef marks order as: InProgress, Ready
        /// Waiter marks order as: Served
        /// </summary>
        Task<bool> UpdateFoodStatusAsync(int transactionId, int newFoodStatusId, string updatedBy, CancellationToken ct = default);

        /// <summary>
        /// Get kitchen statistics for dashboard
        /// </summary>
        Task<KitchenStatsDto> GetKitchenStatsAsync(CancellationToken ct = default);
    }
}
