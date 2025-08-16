using Application.DTOs;


namespace Application.IServices
{
    public interface ICategoryService
    {
        Task<CategoryDto?> GetAsync(Guid id, CancellationToken ct = default);
        Task<List<CategoryDto>> ListAsync(CancellationToken ct = default);
        Task<CategoryDto> CreateAsync(CategoryCreateDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, CategoryUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
