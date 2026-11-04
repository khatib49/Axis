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
    /// Lot / expiry tracking. Batches are an informational layer over the
    /// existing purchases: receiving one never moves stock. Writing an
    /// expired batch off DOES: a Waste movement (reason "Expired") for the
    /// remaining quantity at the default location, then Remaining = 0.
    /// </summary>
    public class StockBatchService : IStockBatchService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<StockBatchService> _logger;

        public StockBatchService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<StockBatchService> logger)
        {
            _db = db; _user = user; _logger = logger;
        }

        public async Task<PaginatedResponse<StockBatchDto>> ListAsync(StockBatchFilterDto filter, CancellationToken ct = default)
        {
            var page = Math.Max(1, filter.Page);
            var pageSize = Math.Clamp(filter.PageSize, 1, 200);

            var q = _db.Set<StockBatch>().AsNoTracking();
            if (filter.IngredientId.HasValue) q = q.Where(b => b.IngredientId == filter.IngredientId.Value);
            if (filter.OnlyWithRemaining == true) q = q.Where(b => b.RemainingQuantity > 0);
            q = Scoped(q);

            var total = await q.CountAsync(ct);
            var rows = await q.OrderBy(b => b.ExpiresOn == null).ThenBy(b => b.ExpiresOn).ThenByDescending(b => b.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return new PaginatedResponse<StockBatchDto>(total, await ToDtosAsync(rows, ct), page, pageSize);
        }

        public async Task<StockBatchDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var b = await _db.Set<StockBatch>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return b == null ? null : (await ToDtosAsync(new[] { b }, ct)).First();
        }

        public async Task<BaseResponse<StockBatchDto>> ReceiveAsync(StockBatchCreateDto dto, CancellationToken ct = default)
        {
            if (dto.Quantity <= 0) return new(false, "Quantity must be greater than zero.", "Quantity must be greater than zero.");
            if (!await _db.Set<Ingredient>().AnyAsync(i => i.Id == dto.IngredientId, ct)) return new(false, "Ingredient not found.", "Ingredient not found.");
            if (dto.PurchaseLineId.HasValue)
            {
                var pl = await _db.Set<PurchaseLine>().AsNoTracking().FirstOrDefaultAsync(p => p.Id == dto.PurchaseLineId.Value, ct);
                if (pl == null) return new(false, "Purchase line not found.", "Purchase line not found.");
                if (pl.IngredientId != dto.IngredientId) return new(false, "Purchase line belongs to a different ingredient.", "Purchase line mismatch.");
            }

            var b = new StockBatch
            {
                IngredientId = dto.IngredientId,
                PurchaseLineId = dto.PurchaseLineId,
                LotNumber = dto.LotNumber?.Trim(),
                Quantity = Math.Round(dto.Quantity, 3),
                RemainingQuantity = Math.Round(dto.Quantity, 3),
                ExpiresOn = Utc(dto.ExpiresOn),
                ReceivedOn = Utc(dto.ReceivedOn) ?? DateTime.UtcNow,
                Notes = dto.Notes?.Trim(),
                BranchId = _user.BranchId
            };
            _db.Set<StockBatch>().Add(b);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Batch received.", (await ToDtosAsync(new[] { b }, ct)).First());
        }

        public async Task<BaseResponse<StockBatchDto>> UpdateAsync(int id, StockBatchUpdateDto dto, CancellationToken ct = default)
        {
            var b = await _db.Set<StockBatch>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b == null) return new(false, "Batch not found.", "Batch not found.");
            if (b.WrittenOffOn.HasValue) return new(false, "A written-off batch cannot be edited.", "Batch written off.");

            if (dto.Quantity.HasValue && dto.Quantity.Value <= 0) return new(false, "Quantity must be greater than zero.", "Quantity must be greater than zero.");
            if (dto.RemainingQuantity.HasValue && dto.RemainingQuantity.Value < 0) return new(false, "Remaining quantity cannot be negative.", "Remaining quantity cannot be negative.");

            if (dto.LotNumber != null) b.LotNumber = dto.LotNumber.Trim();
            if (dto.Quantity.HasValue) b.Quantity = Math.Round(dto.Quantity.Value, 3);
            if (dto.RemainingQuantity.HasValue) b.RemainingQuantity = Math.Round(dto.RemainingQuantity.Value, 3);
            if (dto.ExpiresOn.HasValue) b.ExpiresOn = Utc(dto.ExpiresOn);
            if (dto.ReceivedOn.HasValue) b.ReceivedOn = Utc(dto.ReceivedOn)!.Value;
            if (dto.Notes != null) b.Notes = dto.Notes.Trim();
            if (b.RemainingQuantity > b.Quantity) return new(false, "Remaining quantity cannot exceed the received quantity.", "Remaining > quantity.");

            await _db.SaveChangesAsync(ct);
            return new(true, null, "Batch updated.", (await ToDtosAsync(new[] { b }, ct)).First());
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var b = await _db.Set<StockBatch>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b == null) return false;
            _db.Set<StockBatch>().Remove(b);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<List<StockBatchDto>> GetExpiringAsync(int days = 7, CancellationToken ct = default)
        {
            days = Math.Clamp(days, 0, 365);
            var today = DateTime.UtcNow.Date;
            var limit = today.AddDays(days + 1); // exclusive: whole last day included
            var rows = await Scoped(_db.Set<StockBatch>().AsNoTracking())
                .Where(b => b.RemainingQuantity > 0 && b.ExpiresOn != null && b.ExpiresOn >= today && b.ExpiresOn < limit)
                .OrderBy(b => b.ExpiresOn).ThenBy(b => b.Id)
                .ToListAsync(ct);
            return await ToDtosAsync(rows, ct);
        }

        public async Task<List<StockBatchDto>> GetExpiredAsync(CancellationToken ct = default)
        {
            var today = DateTime.UtcNow.Date;
            var rows = await Scoped(_db.Set<StockBatch>().AsNoTracking())
                .Where(b => b.RemainingQuantity > 0 && b.ExpiresOn != null && b.ExpiresOn < today)
                .OrderBy(b => b.ExpiresOn).ThenBy(b => b.Id)
                .ToListAsync(ct);
            return await ToDtosAsync(rows, ct);
        }

        /// <summary>X-Branch-Id => that branch's batches plus shared (BranchId null) ones; no header => everything.</summary>
        private IQueryable<StockBatch> Scoped(IQueryable<StockBatch> q)
        {
            var branchId = _user.BranchId;
            return branchId.HasValue ? q.Where(b => b.BranchId == null || b.BranchId == branchId.Value) : q;
        }

        public async Task<BaseResponse<StockBatchDto>> WriteOffExpiredAsync(int id, string? notes, CancellationToken ct = default)
        {
            var b = await _db.Set<StockBatch>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (b == null) return new(false, "Batch not found.", "Batch not found.");
            if (b.RemainingQuantity <= 0) return new(false, "Batch has no remaining quantity to write off.", "Nothing to write off.");
            if (!b.ExpiresOn.HasValue) return new(false, "Batch has no expiry date.", "Batch has no expiry date.");
            if (b.ExpiresOn.Value.Date > DateTime.UtcNow.Date) return new(false, "Batch has not expired yet.", "Batch has not expired yet.");

            var ing = await _db.Set<Ingredient>().FirstOrDefaultAsync(i => i.Id == b.IngredientId, ct);
            if (ing == null) return new(false, "Ingredient not found.", "Ingredient not found.");

            var now = DateTime.UtcNow;
            var qty = b.RemainingQuantity;
            var unitCost = ing.BuyPricePerUnit;

            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            ing.QuantityOnHand = Math.Round(ing.QuantityOnHand - qty, 3);
            ing.ModifiedOn = now;

            _db.Set<StockMovement>().Add(new StockMovement
            {
                IngredientId = ing.Id,
                Quantity = -qty,
                Type = "Waste",
                WasteReason = "Expired",
                ReferenceType = "StockBatch",
                ReferenceId = b.Id,
                BalanceAfter = ing.QuantityOnHand,
                UnitCost = unitCost,
                TotalCost = unitCost.HasValue ? Math.Round(qty * unitCost.Value, 2) : null,
                Notes = string.IsNullOrWhiteSpace(notes)
                    ? $"Expired batch {(string.IsNullOrWhiteSpace(b.LotNumber) ? "#" + b.Id : b.LotNumber)} written off"
                    : notes.Trim(),
                CreatedBy = _user.UserName,
                CreatedOn = now
            });

            b.RemainingQuantity = 0m;
            b.WrittenOffOn = now;

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            _logger.LogInformation("StockOps: batch {Id} ({Lot}) written off, {Qty} {Unit} of {Ing}", b.Id, b.LotNumber, qty, ing.Unit, ing.Name);
            return new(true, null, "Batch written off.", (await ToDtosAsync(new[] { b }, ct)).First());
        }

        private static DateTime? Utc(DateTime? d)
        {
            if (!d.HasValue) return null;
            return d.Value.Kind switch
            {
                DateTimeKind.Utc => d.Value,
                DateTimeKind.Local => d.Value.ToUniversalTime(),
                _ => DateTime.SpecifyKind(d.Value, DateTimeKind.Utc)
            };
        }

        private async Task<List<StockBatchDto>> ToDtosAsync(IEnumerable<StockBatch> batches, CancellationToken ct)
        {
            var list = batches.ToList();
            var ingIds = list.Select(b => b.IngredientId).Distinct().ToList();
            var ings = await _db.Set<Ingredient>().AsNoTracking().Where(i => ingIds.Contains(i.Id))
                .Select(i => new { i.Id, i.Name, i.Unit }).ToDictionaryAsync(i => i.Id, ct);
            var today = DateTime.UtcNow.Date;

            return list.Select(b =>
            {
                int? days = b.ExpiresOn.HasValue ? (int)(b.ExpiresOn.Value.Date - today).TotalDays : null;
                return new StockBatchDto(
                    b.Id, b.IngredientId,
                    ings.TryGetValue(b.IngredientId, out var i) ? i.Name : $"#{b.IngredientId}",
                    ings.TryGetValue(b.IngredientId, out var u) ? u.Unit : "",
                    b.PurchaseLineId, b.LotNumber, b.Quantity, b.RemainingQuantity, b.ExpiresOn, b.ReceivedOn, b.Notes,
                    days, days.HasValue && days.Value < 0, b.WrittenOffOn);
            }).ToList();
        }
    }
}
