using Application.DTOs;

namespace Application.IServices
{
    public interface IKitchenService
    {
        /// <summary>
        /// Get all FNB orders for kitchen (excludes TCG Retail)
        /// Filtered by food status and user role (from database mappings)
        /// </summary>
        Task<List<KitchenOrderDto>> GetKitchenOrdersAsync(
            int? foodStatusId = null,
            string? userRole = null,
            CancellationToken ct = default);

        /// <summary>
        /// Get a specific kitchen order by transaction ID
        /// Filtered by user role (from database mappings)
        /// </summary>
        Task<KitchenOrderDto?> GetKitchenOrderByIdAsync(
            int transactionId,
            string? userRole = null,
            CancellationToken ct = default);

        /// <summary>
        /// Update food preparation status
        /// Chef and bartender can update status
        /// </summary>
        Task<bool> UpdateFoodStatusAsync(
            int transactionId,
            int newFoodStatusId,
            string updatedBy,
            CancellationToken ct = default);

        /// <summary>
        /// Get kitchen statistics for dashboard
        /// Filtered by user role if specified
        /// </summary>
        Task<KitchenStatsDto> GetKitchenStatsAsync(
            string? userRole = null,
            CancellationToken ct = default);
    }
}