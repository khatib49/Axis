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
    /// Stocktake sheets. Posting a count at the DEFAULT location updates
    /// Ingredient.QuantityOnHand and writes an Adjustment StockMovement per
    /// non-zero variance (same contract as IngredientService.AdjustStock).
    /// At any other location only the LocationStock rows move.
    /// </summary>
    public class StockCountService : IStockCountService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<StockCountService> _logger;

        public StockCountService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<StockCountService> logger)
        {
            _db = db; _user = user; _logger = logger;
        }

        public async Task<PaginatedResponse<StockCountSummaryDto>> ListAsync(string? status, int? locationId, int page = 1, int pageSize = 25, CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var q = _db.Set<StockCount>().AsNoTracking();
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(c => c.Status == status);
            if (locationId.HasValue) q = q.Where(c => c.LocationId == locationId.Value);

            var total = await q.CountAsync(ct);
            var rows = await q.OrderByDescending(c => c.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(c => new
                {
                    c.Id, c.LocationId, c.Reference, c.Status, c.CreatedOn, c.CreatedBy, c.PostedOn, c.PostedBy, c.Notes,
                    LineCount = c.Lines.Count,
                    CountedLines = c.Lines.Count(l => l.CountedQty != null)
                })
                .ToListAsync(ct);

            var locIds = rows.Select(r => r.LocationId).Distinct().ToList();
            var locs = await _db.Set<StockLocation>().AsNoTracking()
                .Where(l => locIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);

            var data = rows.Select(r => new StockCountSummaryDto(
                r.Id, r.LocationId, locs.GetValueOrDefault(r.LocationId, "?"), r.Reference, r.Status,
                r.CreatedOn, r.CreatedBy, r.PostedOn, r.PostedBy, r.Notes, r.LineCount, r.CountedLines)).ToList();

            return new PaginatedResponse<StockCountSummaryDto>(total, data, page, pageSize);
        }

        public async Task<StockCountDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var c = await _db.Set<StockCount>().AsNoTracking().Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id, ct);
            return c == null ? null : await ToDtoAsync(c, ct);
        }

        public async Task<BaseResponse<StockCountDto>> CreateDraftAsync(StockCountCreateDto dto, CancellationToken ct = default)
        {
            StockLocation? loc;
            if (dto.LocationId.HasValue)
            {
                loc = await _db.Set<StockLocation>().FirstOrDefaultAsync(l => l.Id == dto.LocationId.Value, ct);
                if (loc == null) return new(false, "Location not found.", "Location not found.");
                if (!loc.IsActive) return new(false, "Location is inactive.", "Location is inactive.");
            }
            else
            {
                loc = await StockOpsSupport.EnsureDefaultLocationAsync(_db, _user.BranchId, ct);
            }

            if (await _db.Set<StockCount>().AnyAsync(c => c.LocationId == loc.Id && c.Status == StockOpsSupport.StatusDraft, ct))
                return new(false, "There is already an open stocktake for this location. Post or cancel it first.", "Open stocktake exists.");

            var ingredients = await _db.Set<Ingredient>().AsNoTracking()
                .Where(i => i.IsActive).OrderBy(i => i.Name)
                .Select(i => new { i.Id, i.QuantityOnHand }).ToListAsync(ct);
            if (ingredients.Count == 0)
                return new(false, "No active ingredients to count.", "No active ingredients to count.");

            var qty = await StockOpsSupport.GetQuantitiesAsync(_db, loc, ingredients.Select(i => i.Id).ToList(), ct);

            var count = new StockCount
            {
                LocationId = loc.Id,
                Reference = string.IsNullOrWhiteSpace(dto.Reference) ? "pending" : dto.Reference.Trim(),
                Status = StockOpsSupport.StatusDraft,
                CreatedOn = DateTime.UtcNow,
                CreatedBy = _user.UserName,
                Notes = dto.Notes?.Trim(),
                BranchId = _user.BranchId
            };
            foreach (var i in ingredients)
                count.Lines.Add(new StockCountLine { IngredientId = i.Id, ExpectedQty = qty[i.Id] });

            _db.Set<StockCount>().Add(count);
            await _db.SaveChangesAsync(ct);

            if (count.Reference == "pending")
            {
                count.Reference = StockOpsSupport.Reference("SC", count.Id);
                await _db.SaveChangesAsync(ct);
            }

            return new(true, null, "Stocktake created.", await ToDtoAsync(count, ct));
        }

        public async Task<BaseResponse<StockCountDto>> UpdateLinesAsync(int id, StockCountLinesUpdateDto dto, CancellationToken ct = default)
        {
            var count = await _db.Set<StockCount>().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (count == null) return new(false, "Stocktake not found.", "Stocktake not found.");
            if (count.Status != StockOpsSupport.StatusDraft) return new(false, "Only draft stocktakes can be edited.", "Only draft stocktakes can be edited.");

            var byId = count.Lines.ToDictionary(l => l.Id);
            foreach (var u in dto.Lines ?? new List<StockCountLineUpdateDto>())
            {
                if (!byId.TryGetValue(u.LineId, out var line)) continue;
                if (u.CountedQty.HasValue && u.CountedQty.Value < 0)
                    return new(false, "Counted quantity cannot be negative.", "Counted quantity cannot be negative.");
                line.CountedQty = u.CountedQty.HasValue ? Math.Round(u.CountedQty.Value, 3) : null;
            }

            await _db.SaveChangesAsync(ct);
            return new(true, null, "Counts saved.", await ToDtoAsync(count, ct));
        }

        public async Task<BaseResponse<StockCountDto>> PostAsync(int id, CancellationToken ct = default)
        {
            var count = await _db.Set<StockCount>().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (count == null) return new(false, "Stocktake not found.", "Stocktake not found.");
            if (count.Status != StockOpsSupport.StatusDraft) return new(false, "Only draft stocktakes can be posted.", "Only draft stocktakes can be posted.");

            var counted = count.Lines.Where(l => l.CountedQty.HasValue).ToList();
            if (counted.Count == 0) return new(false, "Enter at least one counted quantity before posting.", "Nothing counted.");

            var loc = await _db.Set<StockLocation>().FirstOrDefaultAsync(l => l.Id == count.LocationId, ct);
            if (loc == null) return new(false, "Location not found.", "Location not found.");

            var ingIds = counted.Select(l => l.IngredientId).ToList();
            var ingredients = await _db.Set<Ingredient>().Where(i => ingIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
            var current = await StockOpsSupport.GetQuantitiesAsync(_db, loc, ingIds, ct);

            var now = DateTime.UtcNow;
            var actor = _user.UserName;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            foreach (var line in counted)
            {
                if (!ingredients.TryGetValue(line.IngredientId, out var ing)) continue;

                // Refresh the expected qty to the live system quantity so the
                // final balance equals exactly what was counted, even if sales
                // or purchases happened while the sheet was open.
                line.ExpectedQty = current[line.IngredientId];
                line.Variance = Math.Round(line.CountedQty!.Value - line.ExpectedQty, 3);
                line.UnitCost = ing.BuyPricePerUnit;
                line.VarianceValue = line.UnitCost.HasValue ? Math.Round(line.Variance * line.UnitCost.Value, 2) : 0m;

                if (line.Variance == 0m) continue;

                if (loc.IsDefault)
                {
                    ing.QuantityOnHand = Math.Round(ing.QuantityOnHand + line.Variance, 3);
                    ing.ModifiedOn = now;

                    _db.Set<StockMovement>().Add(new StockMovement
                    {
                        IngredientId = ing.Id,
                        Quantity = line.Variance,
                        Type = "Adjustment",
                        ReferenceType = "StockCount",
                        ReferenceId = count.Id,
                        BalanceAfter = ing.QuantityOnHand,
                        UnitCost = line.UnitCost,
                        TotalCost = line.UnitCost.HasValue ? line.VarianceValue : null,
                        Notes = $"Stocktake #{count.Reference}",
                        CreatedBy = actor,
                        CreatedOn = now
                    });
                }
                else
                {
                    await StockOpsSupport.AdjustLocationStockAsync(_db, loc.Id, ing.Id, line.Variance, ct);
                }
            }

            count.Status = StockOpsSupport.StatusPosted;
            count.PostedOn = now;
            count.PostedBy = actor;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("StockOps: stocktake {Ref} posted by {User} at location {Loc}", count.Reference, actor, loc.Name);
            return new(true, null, "Stocktake posted.", await ToDtoAsync(count, ct));
        }

        public async Task<BaseResponse<bool>> CancelAsync(int id, CancellationToken ct = default)
        {
            var count = await _db.Set<StockCount>().FirstOrDefaultAsync(c => c.Id == id, ct);
            if (count == null) return new(false, "Stocktake not found.", "Stocktake not found.");
            if (count.Status != StockOpsSupport.StatusDraft) return new(false, "Only draft stocktakes can be cancelled.", "Only draft stocktakes can be cancelled.");
            count.Status = StockOpsSupport.StatusCancelled;
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Stocktake cancelled.", true);
        }

        public async Task<BaseResponse<StockCountVarianceReportDto>> VarianceReportAsync(int id, int top = 10, CancellationToken ct = default)
        {
            var count = await _db.Set<StockCount>().AsNoTracking().Include(c => c.Lines).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (count == null) return new(false, "Stocktake not found.", "Stocktake not found.");
            if (count.Status != StockOpsSupport.StatusPosted) return new(false, "Variance is only available for posted stocktakes.", "Not posted.");

            var dto = await ToDtoAsync(count, ct);
            var counted = dto.Lines.Where(l => l.CountedQty.HasValue).ToList();
            var withVar = counted.Where(l => l.Variance != 0m).ToList();
            var shrink = withVar.Where(l => l.VarianceValue < 0).Sum(l => -l.VarianceValue);
            var surplus = withVar.Where(l => l.VarianceValue > 0).Sum(l => l.VarianceValue);

            var report = new StockCountVarianceReportDto(
                count.Id, count.Reference, dto.LocationName, count.PostedOn,
                counted.Count, withVar.Count,
                withVar.Count(l => l.Variance < 0),
                Math.Round(shrink, 2), Math.Round(surplus, 2), Math.Round(surplus - shrink, 2),
                withVar.OrderByDescending(l => Math.Abs(l.VarianceValue)).ThenByDescending(l => Math.Abs(l.Variance)).Take(Math.Max(1, top)).ToList());
            return new(true, null, "OK", report);
        }

        private async Task<StockCountDto> ToDtoAsync(StockCount c, CancellationToken ct)
        {
            var locName = await _db.Set<StockLocation>().AsNoTracking()
                .Where(l => l.Id == c.LocationId).Select(l => l.Name).FirstOrDefaultAsync(ct) ?? "?";
            var ingIds = c.Lines.Select(l => l.IngredientId).Distinct().ToList();
            var ings = await _db.Set<Ingredient>().AsNoTracking()
                .Where(i => ingIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Name, i.Unit })
                .ToDictionaryAsync(i => i.Id, ct);

            var lines = c.Lines
                .Select(l => new StockCountLineDto(
                    l.Id, l.IngredientId,
                    ings.TryGetValue(l.IngredientId, out var i) ? i.Name : $"#{l.IngredientId}",
                    ings.TryGetValue(l.IngredientId, out var u) ? u.Unit : "",
                    l.ExpectedQty, l.CountedQty, l.Variance, l.UnitCost, l.VarianceValue))
                .OrderBy(l => l.IngredientName)
                .ToList();

            return new StockCountDto(c.Id, c.LocationId, locName, c.Reference, c.Status, c.CreatedOn, c.CreatedBy, c.PostedOn, c.PostedBy, c.Notes, lines);
        }
    }
}
