using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Retail
{
    public class BarcodeService : IBarcodeService
    {
        private readonly ApplicationDbContext _db;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<BarcodeService> _logger;

        public BarcodeService(ApplicationDbContext db, ICurrentUserAccessor user, ILogger<BarcodeService> logger)
        {
            _db = db; _user = user; _logger = logger;
        }

        // ── Queries ────────────────────────────────────────────────────
        private IQueryable<BarcodeDto> Project(IQueryable<ItemBarcode> q)
            => from b in q
               join i in _db.Set<Item>() on b.ItemId equals i.Id
               join v0 in _db.Set<ItemVariant>() on b.VariantId equals (int?)v0.Id into vj
               from v in vj.DefaultIfEmpty()
               select new BarcodeDto(b.Id, b.ItemId, i.Name, b.VariantId, v != null ? v.Name : null,
                   b.Code, b.Symbology, b.IsPrimary, b.CreatedOn, b.CreatedBy);

        public async Task<List<BarcodeDto>> ListAsync(int? itemId, string? search, CancellationToken ct = default)
        {
            var q = _db.Set<ItemBarcode>().AsNoTracking();
            if (itemId.HasValue) q = q.Where(b => b.ItemId == itemId.Value);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = Ean13.Normalise(search);
                q = q.Where(b => b.Code.Contains(term));
            }
            return await Project(q.OrderBy(b => b.ItemId).ThenBy(b => b.VariantId).ThenByDescending(b => b.IsPrimary).ThenBy(b => b.Id))
                .ToListAsync(ct);
        }

        public async Task<BarcodeDto?> GetAsync(int id, CancellationToken ct = default)
            => await Project(_db.Set<ItemBarcode>().AsNoTracking().Where(b => b.Id == id)).FirstOrDefaultAsync(ct);

        // ── Commands ───────────────────────────────────────────────────
        private static string? NormaliseSymbology(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return BarcodeSymbology.Code128;
            var up = s.Trim().ToUpperInvariant().Replace("-", "").Replace(" ", "");
            return BarcodeSymbology.All.Contains(up) ? up : null;
        }

        public async Task<BaseResponse<BarcodeDto>> CreateAsync(BarcodeCreateDto dto, CancellationToken ct = default)
        {
            var code = Ean13.Normalise(dto.Code);
            if (code.Length == 0) return Fail<BarcodeDto>("Code is required.");
            if (code.Length > 64) return Fail<BarcodeDto>("Code is longer than 64 characters.");
            var sym = NormaliseSymbology(dto.Symbology);
            if (sym == null) return Fail<BarcodeDto>("Symbology must be EAN13, CODE128, QR or OTHER.");
            if (sym == BarcodeSymbology.Ean13 && !Ean13.IsValid(code))
                return Fail<BarcodeDto>("Not a valid EAN-13 (13 digits with a correct check digit).");

            var item = await _db.Set<Item>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == dto.ItemId, ct);
            if (item == null) return Fail<BarcodeDto>("Item not found.");
            if (dto.VariantId.HasValue)
            {
                var ok = await _db.Set<ItemVariant>().AnyAsync(v => v.Id == dto.VariantId.Value && v.ItemId == dto.ItemId, ct);
                if (!ok) return Fail<BarcodeDto>("Option does not belong to this item.");
            }
            if (await _db.Set<ItemBarcode>().AnyAsync(b => b.Code == code, ct))
                return Fail<BarcodeDto>($"Code {code} is already assigned.");

            var hasPrimary = await _db.Set<ItemBarcode>()
                .AnyAsync(b => b.ItemId == dto.ItemId && b.VariantId == dto.VariantId && b.IsPrimary, ct);

            var row = new ItemBarcode
            {
                ItemId = dto.ItemId, VariantId = dto.VariantId, Code = code, Symbology = sym,
                IsPrimary = dto.IsPrimary || !hasPrimary,
                CreatedOn = DateTime.UtcNow, CreatedBy = _user.UserName,
            };
            if (dto.IsPrimary && hasPrimary) await ClearPrimaryAsync(dto.ItemId, dto.VariantId, ct);

            _db.Set<ItemBarcode>().Add(row);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Barcode {Code} added to item {ItemId}/{VariantId} by {User}", code, dto.ItemId, dto.VariantId, _user.UserName);
            return new(true, null, "Barcode added.", await GetAsync(row.Id, ct));
        }

        public async Task<BaseResponse<BarcodeDto>> UpdateAsync(int id, BarcodeUpdateDto dto, CancellationToken ct = default)
        {
            var row = await _db.Set<ItemBarcode>().FirstOrDefaultAsync(b => b.Id == id, ct);
            if (row == null) return Fail<BarcodeDto>("Barcode not found.");
            var code = Ean13.Normalise(dto.Code);
            if (code.Length == 0) return Fail<BarcodeDto>("Code is required.");
            if (code.Length > 64) return Fail<BarcodeDto>("Code is longer than 64 characters.");
            var sym = NormaliseSymbology(dto.Symbology);
            if (sym == null) return Fail<BarcodeDto>("Symbology must be EAN13, CODE128, QR or OTHER.");
            if (sym == BarcodeSymbology.Ean13 && !Ean13.IsValid(code))
                return Fail<BarcodeDto>("Not a valid EAN-13 (13 digits with a correct check digit).");
            if (await _db.Set<ItemBarcode>().AnyAsync(b => b.Code == code && b.Id != id, ct))
                return Fail<BarcodeDto>($"Code {code} is already assigned.");

            row.Code = code; row.Symbology = sym;
            if (dto.IsPrimary && !row.IsPrimary)
            {
                await ClearPrimaryAsync(row.ItemId, row.VariantId, ct);
                row.IsPrimary = true;
            }
            else if (!dto.IsPrimary && row.IsPrimary)
            {
                // The only code of an item stays primary — labels need one.
                var others = await _db.Set<ItemBarcode>().CountAsync(b => b.ItemId == row.ItemId && b.VariantId == row.VariantId && b.Id != id, ct);
                if (others > 0) row.IsPrimary = false;
            }
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Barcode updated.", await GetAsync(id, ct));
        }

        public async Task<BaseResponse<BarcodeDto>> SetPrimaryAsync(int id, CancellationToken ct = default)
        {
            var row = await _db.Set<ItemBarcode>().FirstOrDefaultAsync(b => b.Id == id, ct);
            if (row == null) return Fail<BarcodeDto>("Barcode not found.");
            if (!row.IsPrimary)
            {
                await ClearPrimaryAsync(row.ItemId, row.VariantId, ct);
                row.IsPrimary = true;
                await _db.SaveChangesAsync(ct);
            }
            return new(true, null, "Primary code set.", await GetAsync(id, ct));
        }

        public async Task<BaseResponse<bool>> DeleteAsync(int id, CancellationToken ct = default)
        {
            var row = await _db.Set<ItemBarcode>().FirstOrDefaultAsync(b => b.Id == id, ct);
            if (row == null) return new(false, "Barcode not found.", "Barcode not found.", false);
            _db.Set<ItemBarcode>().Remove(row);
            await _db.SaveChangesAsync(ct);
            if (row.IsPrimary)
            {
                // Promote the oldest remaining code so the item keeps a printable one.
                var next = await _db.Set<ItemBarcode>()
                    .Where(b => b.ItemId == row.ItemId && b.VariantId == row.VariantId)
                    .OrderBy(b => b.Id).FirstOrDefaultAsync(ct);
                if (next != null) { next.IsPrimary = true; await _db.SaveChangesAsync(ct); }
            }
            return new(true, null, "Barcode deleted.", true);
        }

        private async Task ClearPrimaryAsync(int itemId, int? variantId, CancellationToken ct)
        {
            var current = await _db.Set<ItemBarcode>()
                .Where(b => b.ItemId == itemId && b.VariantId == variantId && b.IsPrimary).ToListAsync(ct);
            foreach (var c in current) c.IsPrimary = false;
        }

        // ── Lookup ─────────────────────────────────────────────────────
        public async Task<BaseResponse<BarcodeLookupDto>> LookupAsync(string code, CancellationToken ct = default)
        {
            var norm = Ean13.Normalise(code);
            if (norm.Length == 0) return Fail<BarcodeLookupDto>("Scan a code first.");

            // Variant-level codes win over item-level ones (a sleeve colour's
            // own EAN must not resolve to "Sleeves, pick a colour").
            var matches = await _db.Set<ItemBarcode>().AsNoTracking()
                .Where(b => b.Code == norm).ToListAsync(ct);
            var hit = matches.Where(b => b.VariantId != null).OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id).FirstOrDefault()
                      ?? matches.OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id).FirstOrDefault();

            int itemId; int? variantId; int barcodeId = 0; var sym = BarcodeSymbology.Other; string? matchedSerial = null;
            if (hit != null)
            {
                itemId = hit.ItemId; variantId = hit.VariantId; barcodeId = hit.Id; sym = hit.Symbology;
            }
            else
            {
                // Fallback 1: the option's SKU typed on the admin Items screen.
                var sku = await _db.Set<ItemVariant>().AsNoTracking()
                    .Where(v => v.Sku == norm && v.IsActive).OrderBy(v => v.Id).FirstOrDefaultAsync(ct);
                if (sku != null) { itemId = sku.ItemId; variantId = sku.Id; }
                else
                {
                    // Fallback 2: a serial number label scanned at the till.
                    var sn = await _db.Set<SerialNumber>().AsNoTracking()
                        .FirstOrDefaultAsync(s => s.Serial == norm, ct);
                    if (sn == null) return Fail<BarcodeLookupDto>($"No item carries the code {norm}.");
                    if (sn.Status != SerialStatus.InStock)
                        return Fail<BarcodeLookupDto>($"Serial {norm} is {sn.Status}, not in stock.");
                    itemId = sn.ItemId; variantId = sn.VariantId; matchedSerial = sn.Serial;
                }
            }

            var item = await _db.Set<Item>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
            if (item == null) return Fail<BarcodeLookupDto>("The item behind this code no longer exists.");
            if (item.StatusId != 1) return Fail<BarcodeLookupDto>($"{item.Name} is not enabled for sale.");

            ItemVariant? variant = null;
            if (variantId.HasValue)
            {
                variant = await _db.Set<ItemVariant>().AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId.Value, ct);
                if (variant == null || !variant.IsActive) return Fail<BarcodeLookupDto>("The option behind this code is no longer offered.");
            }

            var setting = await _db.Set<SerialSetting>().AsNoTracking().FirstOrDefaultAsync(s => s.ItemId == itemId, ct);
            var requiresSerial = setting?.RequiresSerial ?? false;
            var serialsInStock = await _db.Set<SerialNumber>().CountAsync(s => s.ItemId == itemId && s.Status == SerialStatus.InStock
                                                                              && (variantId == null || s.VariantId == variantId), ct);

            var dto = new BarcodeLookupDto(barcodeId, norm, sym,
                item.Id, item.Name, item.CategoryId, item.ImagePath,
                variant?.Id, variant?.Name,
                Math.Round(item.Price + (variant?.PriceDelta ?? 0m), 2),
                variant?.Quantity ?? item.Quantity,
                requiresSerial, serialsInStock, matchedSerial);
            return new(true, null, variant == null ? item.Name : $"{item.Name} — {variant.Name}", dto);
        }

        // ── Generate ───────────────────────────────────────────────────
        public async Task<BaseResponse<BarcodeDto>> GenerateAsync(BarcodeGenerateDto dto, CancellationToken ct = default)
        {
            var item = await _db.Set<Item>().AsNoTracking().FirstOrDefaultAsync(i => i.Id == dto.ItemId, ct);
            if (item == null) return Fail<BarcodeDto>("Item not found.");
            if (dto.VariantId.HasValue &&
                !await _db.Set<ItemVariant>().AnyAsync(v => v.Id == dto.VariantId.Value && v.ItemId == dto.ItemId, ct))
                return Fail<BarcodeDto>("Option does not belong to this item.");

            var code = Ean13.Build(dto.ItemId, dto.VariantId);
            if (code == null) return Fail<BarcodeDto>("Ids are too large for an in-house EAN-13.");

            var existing = await _db.Set<ItemBarcode>().FirstOrDefaultAsync(b => b.Code == code, ct);
            if (existing != null)
            {
                if (existing.ItemId == dto.ItemId && existing.VariantId == dto.VariantId)
                    return new(true, null, "In-house code already exists.", await GetAsync(existing.Id, ct));
                return Fail<BarcodeDto>($"Code {code} is already used by another item.");
            }

            var hasPrimary = await _db.Set<ItemBarcode>()
                .AnyAsync(b => b.ItemId == dto.ItemId && b.VariantId == dto.VariantId && b.IsPrimary, ct);
            var row = new ItemBarcode
            {
                ItemId = dto.ItemId, VariantId = dto.VariantId, Code = code, Symbology = BarcodeSymbology.Ean13,
                IsPrimary = !hasPrimary, CreatedOn = DateTime.UtcNow, CreatedBy = _user.UserName,
            };
            _db.Set<ItemBarcode>().Add(row);
            await _db.SaveChangesAsync(ct);
            return new(true, null, $"Generated {code}.", await GetAsync(row.Id, ct));
        }

        // ── Labels ─────────────────────────────────────────────────────
        public async Task<List<LabelDto>> LabelsAsync(IEnumerable<int> itemIds, CancellationToken ct = default)
        {
            var ids = itemIds.Where(i => i > 0).Distinct().ToList();
            if (ids.Count == 0) return new();
            var items = await _db.Set<Item>().AsNoTracking().Where(i => ids.Contains(i.Id)).ToListAsync(ct);
            var variants = await _db.Set<ItemVariant>().AsNoTracking().Where(v => ids.Contains(v.ItemId) && v.IsActive)
                .OrderBy(v => v.SortOrder).ThenBy(v => v.Id).ToListAsync(ct);
            var codes = await _db.Set<ItemBarcode>().AsNoTracking().Where(b => ids.Contains(b.ItemId))
                .OrderByDescending(b => b.IsPrimary).ThenBy(b => b.Id).ToListAsync(ct);

            var result = new List<LabelDto>();
            foreach (var id in ids)
            {
                var item = items.FirstOrDefault(i => i.Id == id);
                if (item == null) continue;
                var itemVariants = variants.Where(v => v.ItemId == id).ToList();
                if (itemVariants.Count == 0)
                {
                    var c = codes.FirstOrDefault(b => b.ItemId == id && b.VariantId == null);
                    result.Add(new LabelDto(id, item.Name, null, null, c?.Code, c?.Symbology, item.Price));
                }
                else
                {
                    foreach (var v in itemVariants)
                    {
                        var c = codes.FirstOrDefault(b => b.ItemId == id && b.VariantId == v.Id)
                                ?? codes.FirstOrDefault(b => b.ItemId == id && b.VariantId == null);
                        result.Add(new LabelDto(id, item.Name, v.Id, v.Name, c?.Code, c?.Symbology, Math.Round(item.Price + v.PriceDelta, 2)));
                    }
                }
            }
            return result;
        }

        private static BaseResponse<T> Fail<T>(string msg) => new(false, msg, msg);
    }
}
