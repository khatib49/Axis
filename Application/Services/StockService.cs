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
        // Used by the historical rebuild path (Bug#10) — it needs to walk
        // transactions and their items to re-derive what SHOULD have been
        // consumed given today's recipes.
        private readonly IBaseRepository<TransactionRecord> _txRepo;
        private readonly IBaseRepository<TransactionItem> _txItemRepo;
        private readonly IUnitOfWork _uow;
        private readonly ILogger<StockService> _logger;

        public StockService(
            IBaseRepository<Item> itemRepo,
            IBaseRepository<RecipeLine> recipeRepo,
            IBaseRepository<Ingredient> ingredientRepo,
            IBaseRepository<StockMovement> movementRepo,
            IBaseRepository<TransactionRecord> txRepo,
            IBaseRepository<TransactionItem> txItemRepo,
            IUnitOfWork uow,
            ILogger<StockService> logger)
        {
            _itemRepo = itemRepo;
            _recipeRepo = recipeRepo;
            _ingredientRepo = ingredientRepo;
            _movementRepo = movementRepo;
            _txRepo = txRepo;
            _txItemRepo = txItemRepo;
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

            // Load the ingredient units first — we need them to normalise
            // recipe quantities before aggregating. Otherwise a "beef" recipe
            // in grams would be summed as-is with a "beef" ingredient stored
            // in kilograms, producing 1000× the intended consumption and
            // blowing up COGS. (This was the Bug#9 root cause.)
            var ingredientIds = recipes.Select(r => r.IngredientId).Distinct().ToList();
            var ingredients = await _ingredientRepo.Query(asNoTracking: false)
                .Where(i => ingredientIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);

            var required = new Dictionary<int, decimal>(); // ingredientId -> total qty (in ingredient's unit)
            foreach (var line in lines)
            {
                if (!recipesByItem.TryGetValue(line.itemId, out var rlines)) continue;
                foreach (var r in rlines)
                {
                    if (!ingredients.TryGetValue(r.IngredientId, out var ing))
                    {
                        _logger.LogWarning(
                            "Stock: recipe references missing IngredientId {IngId} for Tx {TxId}",
                            r.IngredientId, transactionId);
                        continue;
                    }

                    // Convert the recipe qty into the ingredient's canonical
                    // unit before multiplying by items sold. r.Unit may be
                    // null on legacy pre-migration rows; fall back to the
                    // ingredient's unit so the converter treats it as no-op.
                    var recipeUnit = r.Unit ?? ing.Unit;
                    var perItem = UnitConverter.Convert(r.Quantity, recipeUnit, ing.Unit, out var ok);
                    if (!ok)
                    {
                        _logger.LogWarning(
                            "Stock: incompatible unit conversion for Recipe {RecipeId} ({From} → {To}) — using raw quantity",
                            r.Id, recipeUnit, ing.Unit);
                    }

                    var need = Math.Round(perItem * line.quantity, 3);
                    if (need <= 0) continue;
                    required[r.IngredientId] = required.GetValueOrDefault(r.IngredientId) + need;
                }
            }

            if (required.Count == 0) return Array.Empty<StockConsumptionWarningDto>();

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

        public async Task RestoreForLinesAsync(
            int transactionId,
            IReadOnlyList<(int itemId, decimal quantity)> lines,
            string? reason,
            string? actor,
            CancellationToken ct = default)
        {
            // Symmetric to ConsumeForOrderAsync, but adds stock back. Used
            // when the admin removes an item from an open invoice (or
            // reduces a line's quantity) — we restore exactly the share
            // that line consumed, based on the item's current recipe.
            if (lines == null || lines.Count == 0) return;

            var itemIds = lines.Select(l => l.itemId).Distinct().ToList();
            var recipes = await _recipeRepo.Query()
                .Where(r => itemIds.Contains(r.ItemId))
                .ToListAsync(ct);
            if (recipes.Count == 0) return; // non-recipe items: nothing to restore on the ingredient side

            // Load ingredients up front so we can normalise recipe qty into
            // each ingredient's unit before aggregating — same pattern as
            // ConsumeForOrderAsync, must stay symmetric or reversals will
            // over/under-shoot the original consumption.
            var ingIdsAll = recipes.Select(r => r.IngredientId).Distinct().ToList();
            var ingredients = await _ingredientRepo.Query(asNoTracking: false)
                .Where(i => ingIdsAll.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);

            // Aggregate per ingredient (in the ingredient's canonical unit).
            var perIngredient = new Dictionary<int, decimal>();
            foreach (var (itemId, qty) in lines)
            {
                if (qty <= 0) continue;
                foreach (var rl in recipes.Where(r => r.ItemId == itemId))
                {
                    if (!ingredients.TryGetValue(rl.IngredientId, out var ing)) continue;
                    var recipeUnit = rl.Unit ?? ing.Unit;
                    var perItem = UnitConverter.Convert(rl.Quantity, recipeUnit, ing.Unit);
                    if (!perIngredient.ContainsKey(rl.IngredientId)) perIngredient[rl.IngredientId] = 0m;
                    perIngredient[rl.IngredientId] += perItem * qty;
                }
            }
            if (perIngredient.Count == 0) return;

            var now = DateTime.UtcNow;
            foreach (var (ingId, qty) in perIngredient)
            {
                if (!ingredients.TryGetValue(ingId, out var ing)) continue;

                ing.QuantityOnHand = Math.Round(ing.QuantityOnHand + qty, 3);
                ing.ModifiedOn = now;

                // Cost mirrors the consume cost basis — use the ingredient's
                // current latest-cost since we don't snapshot per-line cost
                // here. Reverses cleanly in COGS reports.
                var unitCost = ing.BuyPricePerUnit;
                var totalCost = unitCost.HasValue ? -(unitCost.Value * qty) : (decimal?)null;

                await _movementRepo.AddAsync(new StockMovement
                {
                    IngredientId = ing.Id,
                    Quantity = qty,                       // positive — adding back
                    Type = "Consumption",                  // same type so it pairs with the original in reports
                    ReferenceType = "Transaction",
                    ReferenceId = transactionId,
                    BalanceAfter = ing.QuantityOnHand,
                    UnitCost = unitCost,
                    TotalCost = totalCost,
                    Notes = string.IsNullOrWhiteSpace(reason)
                        ? "Partial reversal (line removed from invoice)"
                        : $"Partial reversal: {reason}",
                    CreatedBy = actor,
                    CreatedOn = now
                }, ct);
            }
            // Caller saves.
        }

        // ═══════════════════════════════════════════════════════════════════
        //  HISTORICAL REBUILD — Bug#10
        // -----------------------------------------------------------------
        //  Recomputes every existing Consumption StockMovement using:
        //    (a) the transaction's items (what was actually sold)
        //    (b) each item's CURRENT recipe (units + qtys, post Bug#9 fix)
        //    (c) UnitConverter to normalise recipe unit → ingredient unit
        //    (d) the ingredient's CURRENT BuyPricePerUnit for the cost snapshot
        //
        //  Movements are updated IN PLACE so the audit trail (ReferenceId,
        //  CreatedOn, CreatedBy) is preserved. Notes gets appended with a
        //  rebuild marker so anyone auditing later can see this row was
        //  rewritten.
        //
        //  Ingredient.QuantityOnHand is also adjusted by the net delta —
        //  historical over-consumption gets restored, historical under-
        //  consumption gets debited. Dry-run mode skips ALL writes and just
        //  reports what would happen.
        //
        //  Only Consumption-type movements with ReferenceType="Transaction"
        //  are touched. Purchases, waste, adjustments etc. are left alone.
        // ═══════════════════════════════════════════════════════════════════
        public async Task<RebuildConsumptionCostsResultDto> RebuildConsumptionCostsAsync(
            RebuildConsumptionCostsFilterDto filter,
            string? actor,
            CancellationToken ct = default)
        {
            // Bug: DateTime.Date returns Kind=Unspecified. Npgsql refuses
            // Unspecified when sending to a timestamptz column and the
            // whole request 500s. Coerce every DateTime we ship to EF to
            // Kind=Utc explicitly.
            static DateTime ForceUtc(DateTime d) => d.Kind switch
            {
                DateTimeKind.Utc => d,
                DateTimeKind.Local => d.ToUniversalTime(),
                _ => DateTime.SpecifyKind(d, DateTimeKind.Utc),
            };
            DateTime? from = filter.From.HasValue ? ForceUtc(filter.From.Value) : null;
            DateTime? toExclusive = filter.To.HasValue
                ? ForceUtc(filter.To.Value.Date.AddDays(1))
                : null;

            // 1) Pull every Consumption movement in scope. Read tracked
            //    when we plan to commit; no-tracking otherwise so a dry-
            //    run doesn't churn the change tracker on thousands of rows.
            var movementsQ = _movementRepo.Query(asNoTracking: filter.DryRun)
                .Where(m => m.Type == "Consumption"
                         && m.ReferenceType == "Transaction"
                         && m.ReferenceId != null);
            if (from.HasValue)
                movementsQ = movementsQ.Where(m => m.CreatedOn >= from.Value);
            if (toExclusive.HasValue)
                movementsQ = movementsQ.Where(m => m.CreatedOn < toExclusive.Value);

            var movements = await movementsQ.ToListAsync(ct);
            if (movements.Count == 0)
            {
                return new RebuildConsumptionCostsResultDto(
                    filter.DryRun, from, filter.To, 0, 0, 0, 0m, 0m, 0m,
                    new Dictionary<string, decimal>(),
                    Array.Empty<RebuildLineDto>());
            }

            // 2) Load the transactions and their items in one go so the
            //    per-transaction lookup below is O(1).
            var txIds = movements.Select(m => m.ReferenceId!.Value).Distinct().ToList();
            var txItems = await _txItemRepo.Query()
                .Where(ti => txIds.Contains(ti.TransactionRecordId))
                .Select(ti => new { ti.TransactionRecordId, ti.ItemId, ti.Quantity })
                .ToListAsync(ct);
            var itemsByTx = txItems
                .GroupBy(ti => ti.TransactionRecordId)
                .ToDictionary(g => g.Key, g => g.Select(x => (x.ItemId, x.Quantity)).ToList());

            // 3) Load ALL current recipes for the items involved, and the
            //    ingredients they reference.
            var allItemIds = txItems.Select(ti => ti.ItemId).Distinct().ToList();
            var recipes = await _recipeRepo.Query()
                .Where(r => allItemIds.Contains(r.ItemId))
                .ToListAsync(ct);
            var recipesByItem = recipes.GroupBy(r => r.ItemId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var ingIds = movements.Select(m => m.IngredientId)
                .Concat(recipes.Select(r => r.IngredientId))
                .Distinct().ToList();
            // Track ingredients on commit (we're going to bump QoH); no-track on dry-run.
            var ingredients = await _ingredientRepo.Query(asNoTracking: filter.DryRun)
                .Where(i => ingIds.Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, ct);

            // 4) Walk each movement. Compute what SHOULD be here today,
            //    compare, prepare the update / delta.
            var details = new List<RebuildLineDto>();
            decimal oldTotalCogs = 0m;
            decimal newTotalCogs = 0m;
            int changed = 0;
            var affectedTxIds = new HashSet<int>();

            // ingredientId → net qty delta (positive means Ingredient.QoH should INCREASE)
            var qohDeltaPerIngredient = new Dictionary<int, decimal>();

            foreach (var m in movements)
            {
                var txId = m.ReferenceId!.Value;
                var oldQty = m.Quantity;                              // signed: negative for consumption
                var oldTotal = m.TotalCost ?? 0m;
                oldTotalCogs += oldTotal;

                // For a movement, the "correct" qty is the sum-across-items
                // of recipe_qty (converted) × items_sold, restricted to
                // THIS ingredient. Then applied with the same sign as the
                // original movement (consumption = negative, reversal = positive).
                if (!ingredients.TryGetValue(m.IngredientId, out var ing))
                {
                    // Ingredient deleted — leave the movement alone but
                    // note it in the summary so admin can investigate.
                    newTotalCogs += oldTotal;
                    details.Add(new RebuildLineDto(m.Id, txId, m.IngredientId, "(deleted)",
                        oldQty, oldQty, m.UnitCost, m.UnitCost, oldTotal, oldTotal,
                        "Ingredient no longer exists — skipped"));
                    continue;
                }

                if (!itemsByTx.TryGetValue(txId, out var lines))
                {
                    // Transaction (or its items) no longer exist. Leave it.
                    newTotalCogs += oldTotal;
                    details.Add(new RebuildLineDto(m.Id, txId, m.IngredientId, ing.Name,
                        oldQty, oldQty, m.UnitCost, m.UnitCost, oldTotal, oldTotal,
                        "Transaction/items missing — skipped"));
                    continue;
                }

                decimal correctPositiveQty = 0m; // in ingredient's unit
                foreach (var (itemId, itemQty) in lines)
                {
                    if (!recipesByItem.TryGetValue(itemId, out var rls)) continue;
                    foreach (var r in rls.Where(x => x.IngredientId == m.IngredientId))
                    {
                        var perItem = UnitConverter.Convert(
                            r.Quantity, r.Unit ?? ing.Unit, ing.Unit);
                        correctPositiveQty += perItem * itemQty;
                    }
                }
                correctPositiveQty = Math.Round(correctPositiveQty, 3);

                // Preserve sign: reversal rows have positive Quantity; the
                // "correct" quantity there is a positive number too.
                var isReversal = oldQty > 0m;
                var newQty = isReversal ? correctPositiveQty : -correctPositiveQty;

                var newUnitCost = ing.BuyPricePerUnit;
                var newTotal = newUnitCost.HasValue
                    ? Math.Round(newUnitCost.Value * Math.Abs(newQty) * (isReversal ? -1 : 1), 2)
                    : (decimal?)null;
                // Consumption cost is stored as positive; reversal as negative
                // (mirrors what RestoreForLinesAsync writes today).
                if (newTotal.HasValue && !isReversal) newTotal = Math.Abs(newTotal.Value);
                if (newTotal.HasValue && isReversal) newTotal = -Math.Abs(newTotal.Value);

                newTotalCogs += newTotal ?? 0m;

                var qtyChanged = newQty != oldQty;
                var costChanged = (newTotal ?? 0m) != oldTotal
                                || newUnitCost != m.UnitCost;

                if (qtyChanged || costChanged)
                {
                    changed++;
                    affectedTxIds.Add(txId);

                    // Net QoH delta: how much stock we need to give back
                    // (or take away) to make Ingredient.QuantityOnHand
                    // consistent with the new movement quantities.
                    var qohDelta = newQty - oldQty; // e.g. was -100, now -0.1 → delta +99.9
                    qohDeltaPerIngredient[m.IngredientId] =
                        qohDeltaPerIngredient.GetValueOrDefault(m.IngredientId) + qohDelta;

                    if (!filter.DryRun)
                    {
                        // Entity is tracked (Query with asNoTracking=false).
                        // Assigning properties is enough — EF will detect
                        // the change on SaveChanges. Calling Update() on an
                        // already-tracked entity can raise a duplicate-key
                        // tracking error.
                        m.Quantity = newQty;
                        m.UnitCost = newUnitCost;
                        m.TotalCost = newTotal;
                        m.Notes = $"[rebuilt {DateTime.UtcNow:yyyy-MM-dd}] {m.Notes}";
                    }

                    if (details.Count < filter.DetailLimit)
                    {
                        details.Add(new RebuildLineDto(
                            m.Id, txId, m.IngredientId, ing.Name,
                            oldQty, newQty, m.UnitCost, newUnitCost, oldTotal, newTotal,
                            correctPositiveQty == 0m && !isReversal
                                ? "Item no longer has a recipe line for this ingredient"
                                : "Recipe / unit / cost rebuilt"));
                    }
                }
            }

            // 5) Apply the QoH deltas (not on dry-run).
            var qohReport = new Dictionary<string, decimal>();
            foreach (var (ingId, delta) in qohDeltaPerIngredient)
            {
                if (delta == 0m) continue;
                if (!ingredients.TryGetValue(ingId, out var ing)) continue;
                qohReport[$"{ing.Name} ({ing.Unit})"] = delta;

                if (!filter.DryRun)
                {
                    // Same story — ing is tracked, don't double-track.
                    ing.QuantityOnHand = Math.Round(ing.QuantityOnHand + delta, 3);
                    ing.ModifiedOn = DateTime.UtcNow;
                }
            }

            if (!filter.DryRun && changed > 0)
            {
                await _uow.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Consumption rebuild committed by {Actor}: {Changed} movement(s) updated, {TxCount} tx affected, delta ${Delta}",
                    actor ?? "system", changed, affectedTxIds.Count, newTotalCogs - oldTotalCogs);
            }

            return new RebuildConsumptionCostsResultDto(
                DryRun: filter.DryRun,
                From: from,
                To: filter.To,
                MovementsScanned: movements.Count,
                MovementsChanged: changed,
                TransactionsAffected: affectedTxIds.Count,
                OldTotalCogs: Math.Round(oldTotalCogs, 2),
                NewTotalCogs: Math.Round(newTotalCogs, 2),
                Delta: Math.Round(newTotalCogs - oldTotalCogs, 2),
                QoHAdjustments: qohReport,
                Details: details);
        }
    }
}
