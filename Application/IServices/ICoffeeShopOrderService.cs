using Application.DTOs;

namespace Application.IServices
{
    public interface ICoffeeShopOrderService
    {
        Task<CoffeeShopOrderDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<PaginatedResponse<CoffeeShopOrderDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<CoffeeShopOrderDto> CreateAsync(CoffeeShopOrderCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, CoffeeShopOrderUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
