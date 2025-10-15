using Application.DTOs;

namespace Application.IServices
{
    public interface IMenuService
    {
        Task<IReadOnlyList<ItemMenuDto>> GetItemsByCategoryAsync(int categoryId, string? type = null, CancellationToken ct = default);
        Task<IReadOnlyList<CategoryMenuDto>> GetCategoriesMenuAsync(string? type = null, CancellationToken ct = default);
        Task<IReadOnlyList<CategoryMenuDto>> GetCategoriesMenuAsync(IEnumerable<string>? types, CancellationToken ct = default);
    }
}
