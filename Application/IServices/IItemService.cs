using Application.DTOs;

namespace Application.IServices
{
    public interface IItemService
    {
        Task<ItemDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<PaginatedResponse<ItemDto>> GetByCategoryIdAsync(Guid id, BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<PaginatedResponse<ItemDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<ItemDto> CreateAsync(ItemCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, ItemUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
