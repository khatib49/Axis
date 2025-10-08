using Application.DTOs;

namespace Application.IServices
{
    public interface ICoffeeShopOrderService
    {
        Task<CoffeeShopOrderDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<CoffeeShopOrderDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<CoffeeShopOrderDto> CreateAsync(CoffeeShopOrderCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, CoffeeShopOrderUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
