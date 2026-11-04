using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Shop2
{
    /// <summary>
    /// Read-only storefront queries + the order→Whish bridge. Uses the same
    /// sellable rule as ShopService.GetCatalogAsync (category Type "item" +
    /// ShowInShop, item StatusId 1 + SellOnline) so the two never disagree.
    /// </summary>
    public class Shop2Service : IShop2Service
    {
        private const int EnabledItemStatus = 1;
        private const int OpenInvoiceStatus = 7;
        private const int MaxPageSize = 100;

        private readonly ApplicationDbContext _db;
        private readonly IWhishInvoicePaymentService _whish;
        private readonly ILogger<Shop2Service> _logger;

        public Shop2Service(ApplicationDbContext db, IWhishInvoicePaymentService whish, ILogger<Shop2Service> logger)
        {
            _db = db; _whish = whish; _logger = logger;
        }

        private IQueryable<Item> Sellable() => _db.Set<Item>().AsNoTracking()
            .Where(i => i.StatusId == EnabledItemStatus && i.SellOnline
                        && i.Category != null && i.Category.Type == "item" && i.Category.ShowInShop);

        private static bool TracksStock(Item i) =>
            i.Variants.Any(v => v.IsActive) || string.Equals(i.Category?.ItemType, "Retail", StringComparison.OrdinalIgnoreCase);

        private static ShopItemDto ToDto(Item i) => new(
            i.Id, i.Name, i.Price, i.ImagePath, i.Quantity,
            i.CategoryId, i.Category?.Name ?? "", i.Category?.ItemType,
            TracksStock(i),
            i.Variants.Where(v => v.IsActive).OrderBy(v => v.SortOrder).ThenBy(v => v.Id)
                .Select(v => new ItemVariantDto(v.Id, v.Name, v.Color, v.Sku, v.PriceDelta, v.Quantity, v.IsActive, v.SortOrder)).ToList(),
            i.AddOns.Where(a => a.IsActive).OrderBy(a => a.SortOrder).ThenBy(a => a.Id)
                .Select(a => new ItemAddOnDto(a.Id, a.Name, a.Price, a.IsActive, a.SortOrder)).ToList());

        public async Task<List<ShopCategorySummaryDto>> CategoriesAsync(CancellationToken ct = default)
        {
            var rows = await Sellable()
                .GroupBy(i => new { i.CategoryId, i.Category.Name, i.Category.ItemType })
                .Select(g => new
                {
                    g.Key.CategoryId, g.Key.Name, g.Key.ItemType,
                    Count = g.Count(),
                    MinPrice = g.Min(i => i.Price),
                    Cover = g.Where(i => i.ImagePath != null).OrderBy(i => i.Id).Select(i => i.ImagePath).FirstOrDefault(),
                })
                .OrderBy(g => g.Name)
                .ToListAsync(ct);
            return rows.Select(r => new ShopCategorySummaryDto(r.CategoryId, r.Name, r.ItemType, r.Count, r.Cover, r.MinPrice)).ToList();
        }

        public async Task<PaginatedResponse<ShopItemDto>> ItemsAsync(ShopItemListRequest req, CancellationToken ct = default)
        {
            req ??= new ShopItemListRequest();
            var page = Math.Max(1, req.Page);
            var size = Math.Clamp(req.PageSize <= 0 ? 24 : req.PageSize, 1, MaxPageSize);

            var q = Sellable();
            if (req.CategoryId is int cid) q = q.Where(i => i.CategoryId == cid);
            if (!string.IsNullOrWhiteSpace(req.Search))
            {
                var s = req.Search.Trim().ToLower();
                q = q.Where(i => i.Name.ToLower().Contains(s) || i.Category.Name.ToLower().Contains(s));
            }
            if (req.InStockOnly)
                q = q.Where(i => i.Quantity > 0 || i.Variants.Any(v => v.IsActive && v.Quantity > 0)
                                 || !(i.Category.ItemType != null && i.Category.ItemType.ToLower() == "retail"));

            q = (req.Sort ?? "").Trim().ToLowerInvariant() switch
            {
                "priceasc" => q.OrderBy(i => i.Price).ThenBy(i => i.Name),
                "pricedesc" => q.OrderByDescending(i => i.Price).ThenBy(i => i.Name),
                "newest" => q.OrderByDescending(i => i.Id),
                _ => q.OrderBy(i => i.Name).ThenBy(i => i.Id),
            };

            var total = await q.CountAsync(ct);
            var rows = await q.Skip((page - 1) * size).Take(size)
                .Include(i => i.Category).Include(i => i.Variants).Include(i => i.AddOns)
                .ToListAsync(ct);
            return new PaginatedResponse<ShopItemDto>(total, rows.Select(ToDto).ToList(), page, size);
        }

        public async Task<ShopItemDto?> ItemAsync(int id, CancellationToken ct = default)
        {
            var i = await Sellable().Include(x => x.Category).Include(x => x.Variants).Include(x => x.AddOns)
                .FirstOrDefaultAsync(x => x.Id == id, ct);
            return i is null ? null : ToDto(i);
        }

        public async Task<List<ShopItemDto>> FeaturedAsync(int take, CancellationToken ct = default)
        {
            take = Math.Clamp(take <= 0 ? 8 : take, 1, 24);
            // Items with a picture first, then the newest; sold-out retail items go last.
            var rows = await Sellable()
                .OrderByDescending(i => i.ImagePath != null)
                .ThenByDescending(i => i.Quantity > 0)
                .ThenByDescending(i => i.Id)
                .Take(take)
                .Include(i => i.Category).Include(i => i.Variants).Include(i => i.AddOns)
                .ToListAsync(ct);
            return rows.Select(ToDto).ToList();
        }

        private async Task<(OnlineOrder? order, TransactionRecord? tx)> OwnedOrderWithInvoiceAsync(int userId, string code, CancellationToken ct)
        {
            var c = (code ?? "").Trim();
            if (c.Length == 0) return (null, null);
            var o = await _db.Set<OnlineOrder>().AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.Code == c, ct);
            if (o is null || o.TransactionRecordId is not int txId) return (o, null);
            var tx = await _db.Set<TransactionRecord>().AsNoTracking().FirstOrDefaultAsync(t => t.Id == txId, ct);
            return (o, tx);
        }

        public async Task<ShopOrderInvoiceDto?> OrderInvoiceAsync(int userId, string code, CancellationToken ct = default)
        {
            var (o, tx) = await OwnedOrderWithInvoiceAsync(userId, code, ct);
            if (o is null || tx is null) return null;
            return new ShopOrderInvoiceDto(o.Code, tx.Id, tx.StatusId == OpenInvoiceStatus, Math.Round(tx.TotalPrice, 2), o.Status, o.PaymentMode);
        }

        public async Task<BaseResponse<WhishStartResultDto>> StartWhishAsync(int userId, string code, string successUrl, string cancelUrl, string? uiReturnUrl, CancellationToken ct = default)
        {
            var (o, tx) = await OwnedOrderWithInvoiceAsync(userId, code, ct);
            if (o is null) return new(false, "NotFound", "Order not found.");
            if (tx is null) return new(false, "NoInvoice", "This order has no invoice to pay yet — the shop will confirm it first.");
            if (tx.StatusId != OpenInvoiceStatus) return new(false, "Closed", "This order is already paid.");
            if (o.Status is "Cancelled") return new(false, "Cancelled", "This order was cancelled.");

            _logger.LogInformation("Shop customer {User} starts Whish for order {Code} (invoice {Tx})", userId, o.Code, tx.Id);
            // Source "SelfOrder" = the customer pays their own invoice from their phone; the
            // Payments2 source list is fixed (Till/SelfOrder/Kiosk/Display) and must not change.
            return await _whish.StartAsync(tx.Id, "SelfOrder", successUrl, cancelUrl, uiReturnUrl, ct);
        }
    }
}
