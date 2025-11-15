using Application.DTOs;


namespace Application.IServices
{
    public interface IDiscountService
    {
        Task<DiscountDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<DiscountDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<PaginatedResponse<DiscountDto>> GetByTypeAsync(string type, BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<DiscountDto> CreateAsync(DiscountCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, DiscountUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
