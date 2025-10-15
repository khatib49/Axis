using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class MenuService : IMenuService
    {
        private readonly IBaseRepository<Item> _repoItem;
        private readonly IBaseRepository<Category> _repoCategory;
        public MenuService(IBaseRepository<Item> repoItem, IBaseRepository<Category> repoCategory)
        {
            _repoItem = repoItem;
            _repoCategory = repoCategory;
        }

        // Items for a given category (optionally filter by Type)
        public async Task<IReadOnlyList<ItemMenuDto>> GetItemsByCategoryAsync(int categoryId, string? type = null, CancellationToken ct = default)
        {
            return await _repoItem.Query()
                .Where(i => i.CategoryId == categoryId && (type == null || i.Type == type))
                .OrderBy(i => i.Name)
                .Select(i => new ItemMenuDto(
                    i.Id,
                    i.Name,
                    i.Price,
                    i.Quantity > 0, // Available if stock > 0
                    i.Type,
                    i.ImagePath
                ))
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<CategoryMenuDto>> GetCategoriesMenuAsync(string? type = null, CancellationToken ct = default)
        {
            return await _repoCategory.Query()
                .Where(c => type == null || c.Type == type)
                .OrderBy(c => c.Name)
                .Select(c => new CategoryMenuDto(
                    c.Id,
                    c.Name,
                    c.Type
                ))
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<CategoryMenuDto>> GetCategoriesMenuAsync(IEnumerable<string>? types, CancellationToken ct = default)
        {
            var normalized = Normalize(types);

            return await _repoCategory.Query()
                .Where(c => normalized == null || normalized.Count == 0 || normalized.Contains(c.Type.ToLower()))
                .OrderBy(c => c.Name)
                .Select(c => new CategoryMenuDto(c.Id, c.Name, c.Type))
                .ToListAsync(ct);
        }

        private static List<string>? Normalize(IEnumerable<string>? types)
            => types?
                .Select(t => t?.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .Select(t => t!.ToLower())
                .Distinct()
                .ToList();

    }
}
