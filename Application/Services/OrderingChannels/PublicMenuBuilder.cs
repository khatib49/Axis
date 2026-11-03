using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.OrderingChannels
{
    /// <summary>
    /// Guest-facing menu (QR tables + kiosks) built from the existing
    /// IMenuService data: "item" categories with their items, minus retail
    /// categories and disabled items, minus empty categories.
    /// </summary>
    public class PublicMenuBuilder
    {
        private readonly IMenuService _menu;
        private readonly ApplicationDbContext _db;

        public PublicMenuBuilder(IMenuService menu, ApplicationDbContext db)
        {
            _menu = menu;
            _db = db;
        }

        public async Task<List<SelfOrderMenuCategoryDto>> BuildAsync(CancellationToken ct = default)
        {
            var categories = await _menu.GetCategoriesMenuAsync(new[] { "item" }, ct) ?? Array.Empty<CategoryMenuDto>();

            var retailCategoryIds = await _db.Set<Category>().AsNoTracking()
                .Where(c => c.ItemType != null && c.ItemType.ToLower() == "retail")
                .Select(c => c.Id).ToListAsync(ct);
            var hiddenItemIds = (await _db.Set<Item>().AsNoTracking()
                .Where(i => i.StatusId != 1)
                .Select(i => i.Id).ToListAsync(ct)).ToHashSet();

            var result = new List<SelfOrderMenuCategoryDto>();
            foreach (var c in categories)
            {
                if (retailCategoryIds.Contains(c.Id)) continue;
                var items = (await _menu.GetItemsByCategoryAsync(c.Id, null, ct) ?? Array.Empty<ItemMenuDto>())
                    .Where(i => !hiddenItemIds.Contains(i.Id))
                    .ToList();
                if (items.Count == 0) continue;
                result.Add(new SelfOrderMenuCategoryDto(c.Id, c.Name, items));
            }
            return result;
        }
    }
}
