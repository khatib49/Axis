using Application.DTOs;

namespace Application.IServices
{
    public interface IRecipeService
    {
        // Recipe for a single menu item — empty list means the item has no
        // recipe configured yet (stock tracking is skipped on sale).
        Task<IReadOnlyList<RecipeLineDto>> GetForItemAsync(int itemId, CancellationToken ct = default);

        // Full-replacement upsert. Server diffs against existing lines and
        // applies add/update/remove in one transaction.
        Task<IReadOnlyList<RecipeLineDto>> UpsertAsync(RecipeUpsertRequestDto dto, string? actor, CancellationToken ct = default);

        // Used by the chef "Items without recipes" report.
        Task<IReadOnlyList<int>> GetItemIdsWithoutRecipeAsync(CancellationToken ct = default);
    }
}
