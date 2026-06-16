using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services
{
    /// <summary>
    /// Orchestrates stock consumption / restoration tied to a sale.
    /// Called from the order-creation path inside the same DB transaction.
    /// </summary>
    public class StockService : IStockService
    {
        private readonly IBaseRepository<Item> _itemRepo;
        private readonly IBaseRepository<RecipeLine> _recipeRepo;
        private readonly IBaseRepository<Ingredient> _ingredientRepo;
        private readonly IBaseRepository<StockMovement> _movementRepo;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<StockService> _logger;

        public StockService(
            IBaseRepository<Item> itemRepo,
            IBaseRepository<RecipeLine> recipeRepo,
            IBaseRepository<Ingredient> ingredientRepo,
            IBaseRepository<StockMovement> movementRepo,
            IUnitOfWork uow,
            ILogger<StockService> logger)
        {
            _itemRepo = itemRepo;
            _recipeRepo = recipeRepo;
            _ingredientRepo = ingredientRepo;
            _movementRepo = movementRepo;
            _uow = uow;
            _logger = logger;
        }

        public async Task<IReadOnlyList<StockConsumptionWarningDto>> ConsumeForOrderAsync(
            int transactionId,
            IReadOnlyList<(int itemId, decimal quantity)> lines,
            string? actor,
            CancellationToken ct = default)
        {
            if (lines == null || lines.Count == 0)
                return Array.Empty<StockConsumptionWarningDto>();

            // 1) Pull recipes for these items in one query.
            var itemIds = lines.Select(l => l.itemId).Distinct().ToList();
            var recipes = await _recipeRepo.Query()
                .Where(r => itemIds.Contains(r.ItemId))
                .ToListAsync(ct);

            if (recipes.Count == 0)
            {
                // No recipes configured for ANY of the items — silent skip
                // per the rollout decision. Sales continue normally.
                return Array.Empty<StockConsumptionWarningDto>();
            }

            // 2) Aggregate per-ingredient required quantities across all
            //    sold items. Multiple items in one order may share
            //    ingredients (e.g. two burgers + one cheeseburger all need
            //    beef and buns).
            var recipesByItem = recipes.GroupBy(r => r.ItemId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var required = new Dictionary<int, decimal>(); // ingredientId -> total qty to deduct
            foreach (var line in lines)
            {
                if (!recipesByItem.TryGetValue(line.itemId, out var rlines)) continue;
                foreach (var r in rlines)
                {
                    var need = Math.Round(r.Quantity * line.quantity, 3);
                    if (need <= 0) continue;
                    required[r.IngredientId] = required.GetValueOrDefault(r.IngredientId) + need;
                }
            }

            if (required.Count == 0) return Array.Empty<StockConsumptionWarningDto>();

            // 3) Load + lock the affected ingredients with tracking.
            var ingredientIds = required.Keys.ToList();
            var ingredients = await _ingredientRepo.Query(asNoTracking: false)
                .Where(i => ingredientIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);

            var warnings = new List<StockConsumptionWarningDto>();

            // 4) For each ingredient: subtract, snapshot, write movement.
            foreach (var (ingId, qty) in required)
            {
                if (!ingredients.TryGetValue(ingId, out var ing))
                {
                    // Recipe references an ingredient that's been deleted
                    // somehow — log + skip. Don't block the sale.
                    _logger.LogWarning(
                        "Stock: recipe references missing IngredientId {IngId} for Tx {TxId}",
                        ingId, transactionId);
                    continue;
                }

                ing.QuantityOnHand = Math.Round(ing.QuantityOnHand - qty, 3);
                ing.ModifiedOn = DateTime.UtcNow;

                // Snapshot cost at sale time using the LATEST BuyPricePerUnit.
                // Drives COGS on the accounting dashboard, food-cost %, and
                // inventory valuation. Nullable — if the ingredient has no
                // cost recorded yet (chef hasn't logged a purchase for it),
                // the movement still saves but contributes nothing to COGS.
                var unitCost = ing.BuyPricePerUnit;
                var totalCost = unitCost.HasValue ? Math.Round(qty * unitCost.Value, 2) : (decimal?)null;

                await _movementRepo.AddAsync(new StockMovement
                {
                    IngredientId = ing.Id,
                    Quantity = -qty,
                    Type = "Consumption",
                    ReferenceType = "Transaction",
                    ReferenceId = transactionId,
                    BalanceAfter = ing.QuantityOnHand,
                    UnitCost = unitCost,
                    TotalCost = totalCost,
                    Notes = $"Consumed by sale of {lines.Count} item line(s)",
                    CreatedBy = actor,
                    CreatedOn = DateTime.UtcNow
                }, ct);

                if (ing.QuantityOnHand < 0)
                {
                    warnings.Add(new StockConsumptionWarningDto(
                        ing.Id, ing.Name, ing.Unit, ing.QuantityOnHand));
                }
            }

            // NOTE: SaveChangesAsync is the caller's responsibility — they
            // batch this with the order save in one DB transaction so both
            // succeed or fail together.
            return warnings;
        }

        public async Task RestoreForOrderAsync(int transactionId, string? actor, CancellationToken ct = default)
        {
            // Find every consumption movement for this transaction. For each
            // one, add an opposite-signed movement (so audit trail shows
            // both halves) and bump the ingredient's QuantityOnHand back.
            var original = await _movementRepo.Query(asNoTracking: false)
                .Where(m => m.ReferenceType == "Transaction"
                         && m.ReferenceId == transactionId
                         && m.Type == "Consumption")
                .ToListAsync(ct);

            if (original.Count == 0) return;

            var ingredientIds = original.Select(m => m.IngredientId).Distinct().ToList();
            var ingredients = await _ingredientRepo.Query(asNoTracking: false)
                .Where(i => ingredientIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);

            foreach (var m in original)
            {
                if (!ingredients.TryGetValue(m.IngredientId, out var ing)) continue;

                // The original Quantity was negative; flipping it gives a
                // positive number to add back.
                var reverseQty = -m.Quantity;
                ing.QuantityOnHand = Math.Round(ing.QuantityOnHand + reverseQty, 3);
                ing.ModifiedOn = DateTime.UtcNow;

                // Flip the cost sign too so the audit nets to zero on COGS.
                var reverseTotalCost = m.TotalCost.HasValue ? -m.TotalCost.Value : (decimal?)null;

                await _movementRepo.AddAsync(new StockMovement
                {
                    IngredientId = ing.Id,
                    Quantity = reverseQty,
                    Type = "Consumption", // same type so it pairs visually in the audit
                    ReferenceType = "Transaction",
                    ReferenceId = transactionId,
                    BalanceAfter = ing.QuantityOnHand,
                    UnitCost = m.UnitCost,
                    TotalCost = reverseTotalCost,
                    Notes = $"Reversal of movement #{m.Id} (transaction voided)",
                    CreatedBy = actor,
                    CreatedOn = DateTime.UtcNow
                }, ct);
            }
            // Caller saves.
        }
    }
}
