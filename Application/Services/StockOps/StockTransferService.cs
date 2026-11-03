using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.StockOps
{
    /// <summary>
    /// Inter-location transfers. Leaving the default location writes a
    /// negative Adjustment movement ("Transfer to X") and lowers
    /// Ingredient.QuantityOnHand; arriving at the default location writes a
    /// positive one ("Transfer from X"). Other locations move LocationStock only.
    /// </summary>
    public class StockTransferService : IStockTransferService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<StockTransferService> _logger;

        public StockTransferService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<StockTransferService> logger)
        {
            _db = db; _user = user; _logger = logger;
        }

        public async Task<PaginatedResponse<StockTransferDto>> ListAsync(string? status, int? locationId, int page = 1, int pageSize = 25, CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var q = _db.Set<StockTransfer>().AsNoTracking().Include(t => t.Lines).AsQueryable();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(t => t.Status == status);
            if (locationId.HasValue) q = q.Where(t => t.FromLocationId == locationId.Value || t.ToLocationId == locationId.Value);

            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(t => t.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            var data = new List<StockTransferDto>();
            foreach (var t in rows) data.Add(await ToDtoAsync(t, ct));
            return new PaginatedResponse<StockTransferDto>(total, data, page, pageSize);
        }

        public async Task<StockTransferDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<StockTransfer>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            return t == null ? null : await ToDtoAsync(t, ct);
        }

        public async Task<BaseResponse<StockTransferDto>> CreateAsync(StockTransferCreateDto dto, CancellationToken ct = default)
        {
            var err = await ValidateAsync(dto.FromLocationId, dto.ToLocationId, dto.Lines, ct);
            if (err != null) return new(false, err, err);

            var t = new StockTransfer
            {
                FromLocationId = dto.FromLocationId,
                ToLocationId = dto.ToLocationId,
                Reference = "pending",
                Status = StockOpsSupport.StatusDraft,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = _user.UserName,
                Notes = dto.Notes?.Trim(),
                BranchId = _user.BranchId
            };
            foreach (var l in Merge(dto.Lines))
                t.Lines.Add(new StockTransferLine { IngredientId = l.IngredientId, Quantity = l.Quantity });

            _db.Set<StockTransfer>().Add(t);
            await _db.SaveChangesAsync(ct);
            t.Reference = StockOpsSupport.Reference("TR", t.Id);
            await _db.SaveChangesAsync(ct);

            return new(true, null, "Transfer created.", await ToDtoAsync(t, ct));
        }

        public async Task<BaseResponse<StockTransferDto>> UpdateAsync(int id, StockTransferUpdateDto dto, CancellationToken ct = default)
        {
            var t = await _db.Set<StockTransfer>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t == null) return new(false, "Transfer not found.", "Transfer not found.");
            if (t.Status != StockOpsSupport.StatusDraft) return new(false, "Only draft transfers can be edited.", "Only draft transfers can be edited.");

            var from = dto.FromLocationId ?? t.FromLocationId;
            var to = dto.ToLocationId ?? t.ToLocationId;
            var lines = dto.Lines ?? t.Lines.Select(l => new StockTransferLineInputDto(l.IngredientId, l.Quantity)).ToList();
            var err = await ValidateAsync(from, to, lines, ct);
            if (err != null) return new(false, err, err);

            t.FromLocationId = from;
            t.ToLocationId = to;
            if (dto.Notes != null) t.Notes = dto.Notes.Trim();
            if (dto.Lines != null)
            {
                _db.Set<StockTransferLine>().RemoveRange(t.Lines);
                t.Lines.Clear();
                foreach (var l in Merge(dto.Lines))
                    t.Lines.Add(new StockTransferLine { IngredientId = l.IngredientId, Quantity = l.Quantity });
            }

            await _db.SaveChangesAsync(ct);
            return new(true, null, "Transfer updated.", await ToDtoAsync(t, ct));
        }

        public async Task<BaseResponse<StockTransferDto>> PostAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<StockTransfer>().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t == null) return new(false, "Transfer not found.", "Transfer not found.");
            if (t.Status != StockOpsSupport.StatusDraft) return new(false, "Only draft transfers can be posted.", "Only draft transfers can be posted.");
            if (t.Lines.Count == 0) return new(false, "Transfer has no lines.", "Transfer has no lines.");

            var from = await _db.Set<StockLocation>().FirstOrDefaultAsync(l => l.Id == t.FromLocationId, ct);
            var to = await _db.Set<StockLocation>().FirstOrDefaultAsync(l => l.Id == t.ToLocationId, ct);
            if (from == null || to == null) return new(false, "Location not found.", "Location not found.");
            if (!from.IsActive || !to.IsActive) return new(false, "Both locations must be active.", "Both locations must be active.");

            var ingIds = t.Lines.Select(l => l.IngredientId).Distinct().ToList();
            var ingredients = await _db.Set<Ingredient>().Where(i => ingIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            var available = await StockOpsSupport.GetQuantitiesAsync(_db, from, ingIds, ct);

            // Validate availability at the source BEFORE touching anything.
            foreach (var line in t.Lines)
            {
                if (!ingredients.TryGetValue(line.IngredientId, out var ing))
                    return new(false, $"Ingredient #{line.IngredientId} no longer exists.", "Unknown ingredient.");
                if (available[line.IngredientId] < line.Quantity)
                {
                    var msg = $"Insufficient {ing.Name} at {from.Name}: available {available[line.IngredientId]:0.###} {ing.Unit}, requested {line.Quantity:0.###} {ing.Unit}.";
                    return new(false, msg, msg);
                }
            }

            var now = DateTime.UtcNow;
            var actor = _user.UserName;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            foreach (var line in t.Lines)
            {
                var ing = ingredients[line.IngredientId];
                var unitCost = ing.BuyPricePerUnit;

                // Source side
                if (from.IsDefault)
                {
                    ing.QuantityOnHand = Math.Round(ing.QuantityOnHand - line.Quantity, 3);
                    ing.ModifiedOn = now;
                    _db.Set<StockMovement>().Add(new StockMovement
                    {
                        IngredientId = ing.Id,
                        Quantity = -line.Quantity,
                        Type = "Adjustment",
                        ReferenceType = "StockTransfer",
                        ReferenceId = t.Id,
                        BalanceAfter = ing.QuantityOnHand,
                        UnitCost = unitCost,
                        TotalCost = unitCost.HasValue ? Math.Round(-line.Quantity * unitCost.Value, 2) : null,
                        Notes = $"Transfer to {to.Name} ({t.Reference})",
                        CreatedBy = actor,
                        CreatedOn = now
                    });
                }
                else
                {
                    await StockOpsSupport.AdjustLocationStockAsync(_db, from.Id, ing.Id, -line.Quantity, ct);
                }

                // Destination side
                if (to.IsDefault)
                {
                    ing.QuantityOnHand = Math.Round(ing.QuantityOnHand + line.Quantity, 3);
                    ing.ModifiedOn = now;
                    _db.Set<StockMovement>().Add(new StockMovement
                    {
                        IngredientId = ing.Id,
                        Quantity = line.Quantity,
                        Type = "Adjustment",
                        ReferenceType = "StockTransfer",
                        ReferenceId = t.Id,
                        BalanceAfter = ing.QuantityOnHand,
                        UnitCost = unitCost,
                        TotalCost = unitCost.HasValue ? Math.Round(line.Quantity * unitCost.Value, 2) : null,
                        Notes = $"Transfer from {from.Name} ({t.Reference})",
                        CreatedBy = actor,
                        CreatedOn = now
                    });
                }
                else
                {
                    await StockOpsSupport.AdjustLocationStockAsync(_db, to.Id, ing.Id, line.Quantity, ct);
                }
            }

            t.Status = StockOpsSupport.StatusPosted;
            t.PostedOn = now;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("StockOps: transfer {Ref} posted {From} -> {To} by {User}", t.Reference, from.Name, to.Name, actor);
            return new(true, null, "Transfer posted.", await ToDtoAsync(t, ct));
        }

        public async Task<BaseResponse<bool>> CancelAsync(int id, CancellationToken ct = default)
        {
            var t = await _db.Set<StockTransfer>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (t == null) return new(false, "Transfer not found.", "Transfer not found.");
            if (t.Status != StockOpsSupport.StatusDraft) return new(false, "Only draft transfers can be cancelled.", "Only draft transfers can be cancelled.");
            t.Status = StockOpsSupport.StatusCancelled;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Transfer cancelled.", true);
        }

        private async Task<string?> ValidateAsync(int fromId, int toId, List<StockTransferLineInputDto>? lines, CancellationToken ct)
        {
            if (fromId == toId) return "Source and destination must be different locations.";
            var locs = await _db.Set<StockLocation>().AsNoTracking().Where(l => l.Id == fromId || l.Id == toId).ToListAsync(ct);
            if (locs.Count != 2) return "Location not found.";
            if (locs.Any(l => !l.IsActive)) return "Both locations must be active.";
            if (lines == null || lines.Count == 0) return "Add at least one line.";
            if (lines.Any(l => l.Quantity <= 0)) return "Every line quantity must be greater than zero.";
            var ids = lines.Select(l => l.IngredientId).Distinct().ToList();
            var found = await _db.Set<Ingredient>().AsNoTracking().CountAsync(i => ids.Contains(i.Id), ct);
            if (found != ids.Count) return "One or more ingredients do not exist.";
            return null;
        }

        private static List<StockTransferLineInputDto> Merge(List<StockTransferLineInputDto> lines)
            => lines.GroupBy(l => l.IngredientId)
                .Select(g => new StockTransferLineInputDto(g.Key, Math.Round(g.Sum(x => x.Quantity), 3)))
                .ToList();

        private async Task<StockTransferDto> ToDtoAsync(StockTransfer t, CancellationToken ct)
        {
            var locIds = new[] { t.FromLocationId, t.ToLocationId };
            var locs = await _db.Set<StockLocation>().AsNoTracking().Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);
            var ingIds = t.Lines.Select(l => l.IngredientId).Distinct().ToList();
            var ings = await _db.Set<Ingredient>().AsNoTracking().Where(i => ingIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Name, i.Unit }).ToDictionaryAsync(i => i.Id, ct);

            var lines = t.Lines.Select(l => new StockTransferLineDto(
                l.Id, l.IngredientId,
                ings.TryGetValue(l.IngredientId, out var i) ? i.Name : $"#{l.IngredientId}",
                ings.TryGetValue(l.IngredientId, out var u) ? u.Unit : "",
                l.Quantity)).OrderBy(l => l.IngredientName).ToList();

            return new StockTransferDto(t.Id, t.FromLocationId, locs.GetValueOrDefault(t.FromLocationId, "?"),
                t.ToLocationId, locs.GetValueOrDefault(t.ToLocationId, "?"), t.Reference, t.Status,
                t.CreatedOn, t.CreatedBy, t.PostedOn, t.Notes, lines);
        }
    }
}
