using Application.DTOs;

namespace Application.IServices
{
    /// <summary>
    /// Phase-3 storefront helpers layered on top of the pre-existing shop
    /// (ShopService owns catalogue, auth, checkout and the till inbox; this
    /// service only reads the same data in browsable shapes and bridges a
    /// customer's order to Whish once ShopService has created its invoice).
    /// </summary>
    public interface IShop2Service
    {
        Task<List<ShopCategorySummaryDto>> CategoriesAsync(CancellationToken ct = default);
        Task<PaginatedResponse<ShopItemDto>> ItemsAsync(ShopItemListRequest req, CancellationToken ct = default);
        Task<ShopItemDto?> ItemAsync(int id, CancellationToken ct = default);
        Task<List<ShopItemDto>> FeaturedAsync(int take, CancellationToken ct = default);

        /// <summary>Null when the order is not the customer's or has no invoice yet (never creates one).</summary>
        Task<ShopOrderInvoiceDto?> OrderInvoiceAsync(int userId, string code, CancellationToken ct = default);

        /// <summary>Start a Whish collect for the customer's own order invoice (open invoices only).</summary>
        Task<BaseResponse<WhishStartResultDto>> StartWhishAsync(int userId, string code, string successUrl, string cancelUrl, string? uiReturnUrl, CancellationToken ct = default);
    }
}
