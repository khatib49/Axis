using Application.DTOs;

namespace Application.IServices
{
    public interface IKitchenBarOrderService
    {
        Task<KitchenBarOrderDto?> GetByIdAsync(int id, CancellationToken ct = default);

        Task<PaginatedResponse<KitchenBarOrderDto>> ListAsync(
            KitchenBarOrderListDto filter,
            CancellationToken ct = default);

        Task<bool> UpdateStatusAsync(
            KitchenBarOrderUpdateStatusDto dto,
            CancellationToken ct = default);

        Task<bool> MarkAsPrintedAsync(
            List<int> orderIds,
            CancellationToken ct = default);

        Task<List<KitchenBarOrderDto>> GetPendingOrdersByStationAsync(
            string station,
            CancellationToken ct = default);
    }
}
