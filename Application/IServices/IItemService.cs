using Application.DTOs;

namespace Application.IServices
{
    public interface IItemService
    {
        Task<ItemDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<ItemDto>> GetByCategoryIdAsync(int id, BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<PaginatedResponse<ItemDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<ItemDto> CreateAsync(ItemCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, ItemUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
