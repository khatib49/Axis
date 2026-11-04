namespace Application.DTOs
{
    // ── Storefront browsing (phase 3, anonymous) ─────────────────────────
    /// <summary>A category flagged for the online shop, with how many items it currently sells.</summary>
    public record ShopCategorySummaryDto(int Id, string Name, string? ItemType, int ItemCount, string? CoverImagePath, decimal? MinPrice);

    /// <summary>One sellable item for listing / product pages (catalogue rule: category ShowInShop + item SellOnline + enabled).</summary>
    public record ShopItemDto(
        int Id, string Name, decimal Price, string? ImagePath, int Quantity,
        int CategoryId, string CategoryName, string? ItemType,
        /// True when the shop refuses to sell more than Quantity (retail items or items with variants).
        bool TrackStock,
        List<ItemVariantDto> Variants, List<ItemAddOnDto> AddOns);

    /// <summary>Query for the paged storefront listing. Sort: name | priceAsc | priceDesc | newest.</summary>
    public record ShopItemListRequest(int? CategoryId = null, string? Search = null, int Page = 1, int PageSize = 24, string? Sort = null, bool InStockOnly = false);

    /// <summary>The invoice behind a customer's order — only once ShopService has linked one (till accepted / card settled).</summary>
    public record ShopOrderInvoiceDto(string Code, int TransactionId, bool IsOpen, decimal InvoiceTotal, string OrderStatus, string PaymentMode);

    /// <summary>Body for POST api/shop2/orders/{code}/whish.</summary>
    public record ShopWhishStartRequest(string? ReturnUrl = null);
}
