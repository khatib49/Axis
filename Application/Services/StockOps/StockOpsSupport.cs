using Application.DTOs;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.StockOps
{
    /// <summary>
    /// Shared helpers for the StockOps services: default-location seeding,
    /// per-location quantity lookup, small mappers.
    /// </summary>
    internal static class StockOpsSupport
    {
        public const string DefaultLocationName = "Main";
        public const string DefaultLocationCode = "MAIN";

        public const string StatusDraft = "Draft";
        public const string StatusPosted = "Posted";
        public const string StatusCancelled = "Cancelled";

        /// <summary>
        /// Returns the default location, lazily seeding "Main" when no
        /// location exists at all. The default location mirrors
        /// Ingredient.QuantityOnHand.
        /// </summary>
        public static async Task<StockLocation> EnsureDefaultLocationAsync(ApplicationDbContext db, int? branchId, CancellationToken ct)
        {
            var existing = await db.Set<StockLocation>()
                .OrderByDescending(l => l.IsDefault)
                .ThenBy(l => l.Id)
                .FirstOrDefaultAsync(l => l.IsDefault, ct);
            if (existing != null) return existing;

            var any = await db.Set<StockLocation>().OrderBy(l => l.Id).FirstOrDefaultAsync(ct);
            if (any != null)
            {
                any.IsDefault = true;
                await db.SaveChangesAsync(ct);
                return any;
            }

            var main = new StockLocation
            {
                Name = DefaultLocationName,
                Code = DefaultLocationCode,
                IsDefault = true,
                IsActive = true,
                BranchId = branchId,
                CreatedOn = DateTime.UtcNow
            };
            db.Set<StockLocation>().Add(main);
            await db.SaveChangesAsync(ct);
            return main;
        }

        /// <summary>
        /// Quantity of each ingredient at a location. Default location =>
        /// Ingredient.QuantityOnHand, otherwise the LocationStock row (0 when
        /// absent). Returns an entry for every requested ingredient id.
        /// </summary>
        public static async Task<Dictionary<int, decimal>> GetQuantitiesAsync(
            ApplicationDbContext db, StockLocation location, IReadOnlyCollection<int> ingredientIds, CancellationToken ct)
        {
            var result = ingredientIds.Distinct().ToDictionary(id => id, _ => 0m);
            if (result.Count == 0) return result;

            if (location.IsDefault)
            {
                var ings = await db.Set<Ingredient>().AsNoTracking()
                    .Where(i => ingredientIds.Contains(i.Id))
                    .Select(i => new { i.Id, i.QuantityOnHand })
                    .ToListAsync(ct);
                foreach (var i in ings) result[i.Id] = i.QuantityOnHand;
                return result;
            }

            var rows = await db.Set<LocationStock>().AsNoTracking()
                .Where(s => s.LocationId == location.Id && ingredientIds.Contains(s.IngredientId))
                .Select(s => new { s.IngredientId, s.Quantity })
                .ToListAsync(ct);
            foreach (var r in rows) result[r.IngredientId] = r.Quantity;
            return result;
        }

        /// <summary>
        /// Adds (signed) quantity to a NON-default location's LocationStock
        /// row, creating it when missing. Tracked; caller saves.
        /// </summary>
        public static async Task<LocationStock> AdjustLocationStockAsync(
            ApplicationDbContext db, int locationId, int ingredientId, decimal delta, CancellationToken ct)
        {
            var row = await db.Set<LocationStock>()
                .FirstOrDefaultAsync(s => s.LocationId == locationId && s.IngredientId == ingredientId, ct);
            if (row == null)
            {
                row = new LocationStock { LocationId = locationId, IngredientId = ingredientId, Quantity = 0m };
                db.Set<LocationStock>().Add(row);
            }
            row.Quantity = Math.Round(row.Quantity + delta, 3);
            row.ModifiedOn = DateTime.UtcNow;
            return row;
        }

        public static StockLocationDto ToDto(StockLocation l)
            => new(l.Id, l.Name, l.Code, l.IsDefault, l.IsActive, l.BranchId);

        public static string Reference(string prefix, int id) => $"{prefix}-{id:D5}";
    }
}
