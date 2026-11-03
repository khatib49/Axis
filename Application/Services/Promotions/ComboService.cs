using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Promotions
{
    public class ComboService : IComboService
    {
        private readonly ApplicationDbContext _db;
        private readonly ITransactionRecordService _transactions;
        private readonly IPromotionAutoApplyService _autoApply;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<ComboService> _logger;

        public ComboService(ApplicationDbContext db, ITransactionRecordService transactions,
            IPromotionAutoApplyService autoApply, ICurrentUserAccessor user, ILogger<ComboService> logger)
        {
            _db = db;
            _transactions = transactions;
            _autoApply = autoApply;
            _user = user;
            _logger = logger;
        }

        private async Task<ComboDto> ToDtoAsync(Combo c, CancellationToken ct)
        {
            var ids = c.Items.Select(i => i.ItemId).Distinct().ToList();
            var items = await _db.Set<Item>().AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.Name, i.Price })
                .ToDictionaryAsync(i => i.Id, ct);

            var lines = c.Items.Select(ci =>
            {
                items.TryGetValue(ci.ItemId, out var it);
                return new ComboItemDto(ci.Id, ci.ItemId, it?.Name ?? $"Item #{ci.ItemId}", ci.Quantity, it?.Price ?? 0m);
            }).ToList();

            var itemsTotal = ComboEngine.ItemsTotal(lines.Select(l => (l.UnitPrice, l.Quantity)));
            return new ComboDto(c.Id, c.Name, c.Description, c.Price, c.IsActive, c.ImagePath, c.BranchId, c.CreatedOn,
                itemsTotal, Math.Max(0m, Math.Round(itemsTotal - c.Price, 2)), lines);
        }

        private async Task<string?> ValidateAsync(ComboUpsertDto dto, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required.";
            if (dto.Price < 0) return "Price cannot be negative.";
            var items = dto.Items ?? new List<ComboItemUpsertDto>();
            if (items.Count == 0) return "A combo needs at least one item.";
            if (items.Any(i => i.Quantity <= 0)) return "Every item quantity must be greater than zero.";
            if (items.Select(i => i.ItemId).Distinct().Count() != items.Count) return "An item is listed twice; merge the quantities.";
            var ids = items.Select(i => i.ItemId).ToList();
            var found = await _db.Set<Item>().AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct);
            var missing = ids.Except(found).ToList();
            return missing.Count > 0 ? $"Unknown item id(s): {string.Join(", ", missing)}" : null;
        }

        private static void Apply(Combo c, ComboUpsertDto dto)
        {
            c.Name = dto.Name.Trim();
            c.Description = dto.Description?.Trim();
            c.Price = Math.Round(dto.Price, 2);
            c.IsActive = dto.IsActive;
            c.ImagePath = dto.ImagePath;
        }

        // ── admin CRUD ───────────────────────────────────────────────────
        public async Task<List<ComboDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
        {
            var q = _db.Set<Combo>().AsNoTracking().Include(c => c.Items).AsQueryable();
            if (!includeInactive) q = q.Where(c => c.IsActive);
            var rows = await q.OrderBy(c => c.Name).ToListAsync(ct);
            var list = new List<ComboDto>(rows.Count);
            foreach (var r in rows) list.Add(await ToDtoAsync(r, ct));
            return list;
        }

        public async Task<ComboDto?> GetAsync(int id, CancellationToken ct = default)
        {
            var c = await _db.Set<Combo>().AsNoTracking().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);
            return c is null ? null : await ToDtoAsync(c, ct);
        }

        public async Task<BaseResponse<ComboDto>> CreateAsync(ComboUpsertDto dto, CancellationToken ct = default)
        {
            var err = await ValidateAsync(dto, ct);
            if (err != null) return new BaseResponse<ComboDto>(false, "Validation", err);
            var c = new Combo { BranchId = _user.BranchId, CreatedOn = DateTime.UtcNow };
            Apply(c, dto);
            foreach (var i in dto.Items!) c.Items.Add(new ComboItem { ItemId = i.ItemId, Quantity = i.Quantity });
            _db.Add(c);
            await _db.SaveChangesAsync(ct);
            return new BaseResponse<ComboDto>(true, null, "Combo created", await ToDtoAsync(c, ct));
        }

        public async Task<BaseResponse<ComboDto>> UpdateAsync(int id, ComboUpsertDto dto, CancellationToken ct = default)
        {
            var err = await ValidateAsync(dto, ct);
            if (err != null) return new BaseResponse<ComboDto>(false, "Validation", err);
            var c = await _db.Set<Combo>().Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return new BaseResponse<ComboDto>(false, "Not found", "Combo not found.");
            Apply(c, dto);

            var wanted = dto.Items!.ToDictionary(i => i.ItemId, i => i.Quantity);
            foreach (var existing in c.Items.ToList())
            {
                if (wanted.TryGetValue(existing.ItemId, out var qty)) { existing.Quantity = qty; wanted.Remove(existing.ItemId); }
                else { c.Items.Remove(existing); _db.Remove(existing); }
            }
            foreach (var (itemId, qty) in wanted) c.Items.Add(new ComboItem { ComboId = c.Id, ItemId = itemId, Quantity = qty });

            await _db.SaveChangesAsync(ct);
            return new BaseResponse<ComboDto>(true, null, "Combo updated", await ToDtoAsync(c, ct));
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var c = await _db.Set<Combo>().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return false;
            _db.Remove(c);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        // ── till ─────────────────────────────────────────────────────────
        public async Task<BaseResponse<ComboSellResultDto>> SellAsync(ComboSellDto dto, CancellationToken ct = default)
        {
            var combo = await _db.Set<Combo>().AsNoTracking().Include(c => c.Items).FirstOrDefaultAsync(c => c.Id == dto.ComboId, ct);
            if (combo is null) return new BaseResponse<ComboSellResultDto>(false, "Not found", "Combo not found.");
            if (!combo.IsActive) return new BaseResponse<ComboSellResultDto>(false, "Inactive", "This combo is not active.");
            if (combo.Items.Count == 0) return new BaseResponse<ComboSellResultDto>(false, "Empty combo", "This combo has no items.");

            var ids = combo.Items.Select(i => i.ItemId).ToList();
            var prices = await _db.Set<Item>().AsNoTracking()
                .Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Price }).ToDictionaryAsync(i => i.Id, i => i.Price, ct);
            if (prices.Count != ids.Distinct().Count())
                return new BaseResponse<ComboSellResultDto>(false, "Invalid combo", "One of the combo's items no longer exists.");

            var itemsTotal = ComboEngine.ItemsTotal(combo.Items.Select(i => (prices[i.ItemId], i.Quantity)));
            var actor = _user.UserName;
            var request = combo.Items.Select(i => new OrderItemRequest(i.ItemId, i.Quantity)).ToList();

            // 1) Lines go through the EXISTING order path (stock, recipes, kitchen tickets).
            BaseResponse<TransactionDto> orderResult;
            var createdNew = !dto.TransactionId.HasValue;
            if (dto.TransactionId.HasValue)
            {
                var trx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.TransactionId.Value, ct);
                if (trx is null) return new BaseResponse<ComboSellResultDto>(false, "Invalid invoice", "The specified invoice does not exist.");
                if (trx.StatusId != PromotionInvoiceAdjuster.OpenInvoiceStatusId)
                    return new BaseResponse<ComboSellResultDto>(false, "Invoice not open", "A combo can only be added to an open invoice.");
                orderResult = await _transactions.AddItemsToOpenInvoice(trx.Id, request, actor, ct);
            }
            else
            {
                orderResult = await _transactions.CreateCoffeeShopOrder(dto.UserId, 0, request, actor, ct,
                    comment: $"Combo: {combo.Name}", isOpenInvoice: true);
            }

            if (!orderResult.Success || orderResult.Data is null)
                return new BaseResponse<ComboSellResultDto>(false, orderResult.Error ?? "Order failed", orderResult.Message);

            var txId = orderResult.Data.Id;

            // 2) Phase 2: the automatic pass re-counts every combo set on the
            //    invoice (and happy hour) and books only the difference, so selling
            //    the same combo twice or mixing it with loose lines never double-books.
            var auto = await _autoApply.ApplyAsync(txId, actor, ct);
            if (!auto.Success || auto.Data is null)
            {
                // Lines are already on the invoice; report the problem rather than hide it.
                _logger.LogWarning("Combo {ComboId} lines added to invoice {TxId} but promotions not applied: {Err}", combo.Id, txId, auto.Message);
                return new BaseResponse<ComboSellResultDto>(false, "Partial", $"Items were added but the combo price could not be applied: {auto.Message}",
                    new ComboSellResultDto(combo.Id, combo.Name, txId, createdNew, itemsTotal, combo.Price, 0m, orderResult.Data.TotalPrice, orderResult.Data));
            }

            var booked = auto.Data.Combos.FirstOrDefault(c => c.ComboId == combo.Id);
            var applied = booked?.Delta ?? 0m;
            var totalAfter = auto.Data.TotalAfter;

            _logger.LogInformation("Combo {ComboId} sold on invoice {TxId}; delta {Delta}", combo.Id, txId, applied);
            return new BaseResponse<ComboSellResultDto>(true, null, "Combo sold",
                new ComboSellResultDto(combo.Id, combo.Name, txId, createdNew, itemsTotal, combo.Price, applied, totalAfter, orderResult.Data));
        }
    }
}
