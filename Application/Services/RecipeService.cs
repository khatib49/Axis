using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    public class RecipeService : IRecipeService
    {
        private readonly IBaseRepository<RecipeLine> _lineRepo;
        private readonly IBaseRepository<Item> _itemRepo;
        private readonly IBaseRepository<Ingredient> _ingredientRepo;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<RecipeService> _logger;

        public RecipeService(
            IBaseRepository<RecipeLine> lineRepo,
            IBaseRepository<Item> itemRepo,
            IBaseRepository<Ingredient> ingredientRepo,
            IUnitOfWork uow,
            ILogger<RecipeService> logger)
        {
            _lineRepo = lineRepo;
            _itemRepo = itemRepo;
            _ingredientRepo = ingredientRepo;
            _uow = uow;
            _logger = logger;
        }

        public async Task<IReadOnlyList<RecipeLineDto>> GetForItemAsync(int itemId, CancellationToken ct = default)
        {
            return await _lineRepo.Query()
                .Include(l => l.Ingredient)
                .Where(l => l.ItemId == itemId)
                .OrderBy(l => l.Ingredient.Name)
                .Select(l => new RecipeLineDto(
                    l.Id, l.ItemId, l.IngredientId, l.Ingredient.Name, l.Ingredient.Unit,
                    l.Quantity, l.Notes))
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<RecipeLineDto>> UpsertAsync(RecipeUpsertRequestDto dto, string? actor, CancellationToken ct = default)
        {
            if (dto.Lines == null) throw new ArgumentException("Lines payload is required.");

            // Validate Item exists.
            var itemExists = await _itemRepo.Query().AnyAsync(i => i.Id == dto.ItemId, ct);
            if (!itemExists) throw new KeyNotFoundException("Item not found.");

            // Dedupe lines by IngredientId — the DB has a unique constraint
            // on (ItemId, IngredientId) and we want to surface a clean error
            // instead of a constraint violation.
            var grouped = dto.Lines
                .Where(l => l.Quantity > 0)
                .GroupBy(l => l.IngredientId)
                .Select(g => g.Last())
                .ToList();

            if (grouped.Count == 0)
            {
                // Caller wants to clear the recipe entirely.
                var existingAll = await _lineRepo.Query(asNoTracking: false)
                    .Where(l => l.ItemId == dto.ItemId)
                    .ToListAsync(ct);
                foreach (var ex in existingAll) _lineRepo.Remove(ex);
                await _uow.SaveChangesAsync(ct);
                return new List<RecipeLineDto>();
            }

            // Validate every ingredient referenced exists + is active.
            var ingredientIds = grouped.Select(l => l.IngredientId).Distinct().ToList();
            var validIds = await _ingredientRepo.Query()
                .Where(i => ingredientIds.Contains(i.Id) && i.IsActive)
                .Select(i => i.Id)
                .ToListAsync(ct);
            var invalid = ingredientIds.Except(validIds).ToList();
            if (invalid.Any())
                throw new ArgumentException($"Invalid or inactive ingredient ids: {string.Join(", ", invalid)}");

            // Diff: existing vs incoming.
            var existing = await _lineRepo.Query(asNoTracking: false)
                .Where(l => l.ItemId == dto.ItemId)
                .ToListAsync(ct);
            var existingByIng = existing.ToDictionary(e => e.IngredientId);

            foreach (var line in grouped)
            {
                if (existingByIng.TryGetValue(line.IngredientId, out var current))
                {
                    current.Quantity = Math.Round(line.Quantity, 3);
                    current.Notes = line.Notes?.Trim();
                    _lineRepo.Update(current);
                    existingByIng.Remove(line.IngredientId);
                }
                else
                {
                    await _lineRepo.AddAsync(new RecipeLine
                    {
                        ItemId = dto.ItemId,
                        IngredientId = line.IngredientId,
                        Quantity = Math.Round(line.Quantity, 3),
                        Notes = line.Notes?.Trim(),
                        CreatedOn = DateTime.UtcNow
                    }, ct);
                }
            }

            // Anything left in existingByIng is a removal.
            foreach (var toRemove in existingByIng.Values)
                _lineRepo.Remove(toRemove);

            await _uow.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Recipe upsert for Item {ItemId} by {Actor}: {Count} lines",
                dto.ItemId, actor ?? "(anon)", grouped.Count);

            return await GetForItemAsync(dto.ItemId, ct);
        }

        public async Task<IReadOnlyList<int>> GetItemIdsWithoutRecipeAsync(CancellationToken ct = default)
        {
            // Items with no RecipeLine — the chef's "Items without recipes"
            // report so they can see what's still uncovered.
            // NOTE: cannot call _lineRepo.Query() inside the expression tree
            // because Query() has an optional argument (asNoTracking) which
            // C# refuses to encode into an Expression. Use the navigation
            // collection instead — EF Core translates this to a NOT EXISTS
            // subquery against the RecipeLines table.
            return await _itemRepo.Query()
                .Where(i => !i.RecipeLines.Any())
                .Select(i => i.Id)
                .ToListAsync(ct);
        }
    }
}
