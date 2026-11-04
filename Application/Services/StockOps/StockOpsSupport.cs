using Application.DTOs;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services.StockOps
{
    /// <summary>
    /// Shared helpers for the StockOps services: default-location seeding,
    /// per-location quantity lookup, branch scoping, small mappers.
    ///
    /// Phase 3 — branch model:
    ///   * The GLOBAL default location (IsDefault, BranchId == null, "Main")
    ///     mirrors Ingredient.QuantityOnHand exactly as before.
    ///   * Every branch gets its own default location (IsDefault, BranchId == branch,
    ///     "Main – CODE"), created lazily. Branch defaults hold their own
    ///     LocationStock rows: nothing posted against them touches the ingredient.
    ///   * With an X-Branch-Id header, lists show that branch's locations plus the
    ///     BranchId-null (shared) ones. No header = everything.
    /// </summary>
    internal static class StockOpsSupport
    {
        public const string DefaultLocationName = "Main";
        public const string DefaultLocationCode = "MAIN";

        public const string StatusDraft = "Draft";
        public const string StatusPosted = "Posted";
        public const string StatusCancelled = "Cancelled";

        /// <summary>True when the location mirrors Ingredient.QuantityOnHand (global default only).</summary>
        public static bool MirrorsIngredient(StockLocation l) => l.IsDefault && l.BranchId == null;

        /// <summary>
        /// Returns the GLOBAL default location (BranchId null), lazily seeding
        /// "Main" when no shared location exists at all. The branchId argument
        /// is kept for signature compatibility; the global default is never
        /// branch-scoped (use <see cref="GetDefaultLocationAsync"/> for that).
        /// </summary>
        public static async Task<StockLocation> EnsureDefaultLocationAsync(ApplicationDbContext db, int? branchId, CancellationToken ct)
        {
            var existing = await db.Set<StockLocation>()
                .Where(l => l.IsDefault && l.BranchId == null)
                .OrderBy(l => l.Id)
                .FirstOrDefaultAsync(ct);
            if (existing != null) return existing;

            var anyShared = await db.Set<StockLocation>().AnyAsync(l => l.BranchId == null, ct);
            if (!anyShared)
            {
                // Legacy fallback (phase 2 seeded "Main" with the caller's header):
                // when the only default location carries a branch id and no shared
                // location exists, it is the one mirroring the ingredient table.
                var legacy = await db.Set<StockLocation>().Where(l => l.IsDefault).OrderBy(l => l.Id).ToListAsync(ct);
                if (legacy.Count == 1)
                {
                    legacy[0].BranchId = null;
                    await db.SaveChangesAsync(ct);
                    return legacy[0];
                }
            }

            var any = await db.Set<StockLocation>().Where(l => l.BranchId == null).OrderBy(l => l.Id).FirstOrDefaultAsync(ct);
            if (any != null)
            {
                any.IsDefault = true;
                await db.SaveChangesAsync(ct);
                return any;
            }

            var main = new StockLocation
            {
                Name = DefaultLocationName,
                Code = await UniqueCodeAsync(db, DefaultLocationCode, ct),
                IsDefault = true,
                IsActive = true,
                BranchId = null,
                CreatedOn = DateTime.UtcNow
            };
            db.Set<StockLocation>().Add(main);
            await db.SaveChangesAsync(ct);
            return main;
        }

        /// <summary>
        /// Default location for a branch: the IsDefault location whose BranchId
        /// equals <paramref name="branchId"/>, created lazily as "Main – CODE".
        /// Null (or an unknown branch) returns the global default.
        /// </summary>
        public static async Task<StockLocation> GetDefaultLocationAsync(ApplicationDbContext db, int? branchId, CancellationToken ct)
        {
            if (!branchId.HasValue) return await EnsureDefaultLocationAsync(db, null, ct);

            var existing = await db.Set<StockLocation>()
                .Where(l => l.IsDefault && l.BranchId == branchId.Value)
                .OrderBy(l => l.Id)
                .FirstOrDefaultAsync(ct);
            if (existing != null) return existing;

            var branch = await db.Set<Branch>().AsNoTracking().FirstOrDefaultAsync(b => b.Id == branchId.Value, ct);
            if (branch == null) return await EnsureDefaultLocationAsync(db, null, ct);

            // Make sure the global default exists first so ids stay predictable.
            await EnsureDefaultLocationAsync(db, null, ct);

            var loc = new StockLocation
            {
                Name = $"{DefaultLocationName} – {branch.Code}",
                Code = await UniqueCodeAsync(db, $"{DefaultLocationCode}-{branch.Code}", ct),
                IsDefault = true,
                IsActive = true,
                BranchId = branch.Id,
                CreatedOn = DateTime.UtcNow
            };
            db.Set<StockLocation>().Add(loc);
            await db.SaveChangesAsync(ct);
            return loc;
        }

        private static async Task<string> UniqueCodeAsync(ApplicationDbContext db, string wanted, CancellationToken ct)
        {
            var code = wanted.ToUpperInvariant();
            if (code.Length > 20) code = code[..20];
            if (!await db.Set<StockLocation>().AnyAsync(l => l.Code == code, ct)) return code;
            for (var i = 2; i < 100; i++)
            {
                var candidate = $"{code[..Math.Min(code.Length, 17)]}-{i}";
                if (!await db.Set<StockLocation>().AnyAsync(l => l.Code == candidate, ct)) return candidate;
            }
            return $"L{DateTime.UtcNow.Ticks % 100000000}";
        }

        /// <summary>
        /// Ids of locations that belong to ANOTHER branch than <paramref name="branchId"/>.
        /// A row is visible for the branch when none of its locations is in this set
        /// (i.e. it only touches the branch's own or shared locations). Empty when
        /// no branch is selected (no header = everything).
        /// </summary>
        public static async Task<List<int>> OtherBranchLocationIdsAsync(ApplicationDbContext db, int? branchId, CancellationToken ct)
        {
            if (!branchId.HasValue) return new List<int>();
            return await db.Set<StockLocation>().AsNoTracking()
                .Where(l => l.BranchId != null && l.BranchId != branchId.Value)
                .Select(l => l.Id)
                .ToListAsync(ct);
        }

        /// <summary>
        /// Quantity of each ingredient at a location. Global default =>
        /// Ingredient.QuantityOnHand, otherwise the LocationStock row (0 when
        /// absent). Returns an entry for every requested ingredient id.
        /// </summary>
        public static async Task<Dictionary<int, decimal>> GetQuantitiesAsync(
            ApplicationDbContext db, StockLocation location, IReadOnlyCollection<int> ingredientIds, CancellationToken ct)
        {
            var result = ingredientIds.Distinct().ToDictionary(id => id, _ => 0m);
            if (result.Count == 0) return result;

            if (MirrorsIngredient(location))
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
        /// Adds (signed) quantity to a non-mirroring location's LocationStock
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
