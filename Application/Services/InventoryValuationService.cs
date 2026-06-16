using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class InventoryValuationService : IInventoryValuationService
    {
        private readonly IBaseRepository<Ingredient> _ingredientRepo;
        private readonly IBaseRepository<StockMovement> _movementRepo;

        public InventoryValuationService(
            IBaseRepository<Ingredient> ingredientRepo,
            IBaseRepository<StockMovement> movementRepo)
        {
            _ingredientRepo = ingredientRepo;
            _movementRepo = movementRepo;
        }

        public async Task<InventoryValuationDto> GetAsync(DateTime? from, DateTime? to, CancellationToken ct = default)
        {
            var ingredients = await _ingredientRepo.Query()
                .Where(i => i.IsActive)
                .OrderBy(i => i.Name)
                .ToListAsync(ct);

            // 1. By ingredient — current value
            var byIngredient = ingredients
                .Select(i =>
                {
                    var value = (i.QuantityOnHand > 0 && i.BuyPricePerUnit.HasValue)
                        ? Math.Round(i.QuantityOnHand * i.BuyPricePerUnit.Value, 2)
                        : 0m;
                    return new InventoryValueLineDto(
                        i.Id, i.Name, i.Unit, i.QuantityOnHand, i.BuyPricePerUnit, value);
                })
                .OrderByDescending(x => x.Value)
                .ToList();

            var totalValue = byIngredient.Sum(x => x.Value);

            // 2. Consumption window: defaults to last 30 days if not given.
            var rangeFrom = from ?? DateTime.UtcNow.AddDays(-30);
            var rangeTo = to ?? DateTime.UtcNow;
            var rangeToExclusive = rangeTo.Date.AddDays(1);

            var consumptions = await _movementRepo.Query()
                .Include(m => m.Ingredient)
                .Where(m => m.Type == "Consumption"
                         && m.CreatedOn >= rangeFrom
                         && m.CreatedOn < rangeToExclusive)
                .ToListAsync(ct);

            // 3. Top movers — most consumed by quantity (and value).
            var topMovers = consumptions
                .GroupBy(m => m.IngredientId)
                .Select(g => new InventoryTopMoverDto(
                    g.Key,
                    g.First().Ingredient.Name,
                    g.First().Ingredient.Unit,
                    Math.Round(g.Sum(x => Math.Abs(x.Quantity)), 3),
                    Math.Round(g.Sum(x => x.TotalCost ?? 0m), 2)))
                .OrderByDescending(x => x.ConsumedValue)
                .Take(15)
                .ToList();

            // 4. Slow movers — active ingredients with value > 0 that had
            //    no consumption in the period (or none ever).
            var consumedIds = consumptions.Select(c => c.IngredientId).Distinct().ToHashSet();

            // Latest consumption date per ingredient (lifetime, not just window)
            var lastConsByIng = await _movementRepo.Query()
                .Where(m => m.Type == "Consumption")
                .GroupBy(m => m.IngredientId)
                .Select(g => new { Id = g.Key, Last = g.Max(x => x.CreatedOn) })
                .ToDictionaryAsync(x => x.Id, x => x.Last, ct);

            var slowMovers = byIngredient
                .Where(b => b.Value > 0 && !consumedIds.Contains(b.IngredientId))
                .Select(b => new InventorySlowMoverDto(
                    b.IngredientId, b.IngredientName, b.Unit,
                    b.QuantityOnHand, b.UnitCost, b.Value,
                    lastConsByIng.TryGetValue(b.IngredientId, out var d) ? d : (DateTime?)null))
                .OrderByDescending(x => x.Value)
                .Take(15)
                .ToList();

            return new InventoryValuationDto(
                From: rangeFrom,
                To: rangeTo,
                TotalValue: totalValue,
                IngredientCount: byIngredient.Count,
                ByIngredient: byIngredient,
                TopMovers: topMovers,
                SlowMovers: slowMovers);
        }
    }
}
