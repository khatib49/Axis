using Application.DTOs;


namespace Application.IServices
{
    public interface ICategoryService
    {
        Task<CategoryDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<CategoryDto>> ListAsync(BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<PaginatedResponse<CategoryDto>> GetByTypeAsync(string type, BasePaginationRequestDto pagination, CancellationToken ct = default);
        Task<CategoryDto> CreateAsync(CategoryCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(int id, CategoryUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    }
}
