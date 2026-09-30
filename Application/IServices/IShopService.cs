using Application.DTOs;

namespace Application.IServices
{
    /// <summary>Website customers: account, cart checkout, order tracking; plus the till's order inbox.</summary>
    public interface IShopService
    {
        // ── Public catalogue (anonymous) ─────────────────────────────────
        /// <summary>Categories flagged ShowInShop with their sellable items, plus shop/delivery switches and zones.</summary>
        Task<ShopCatalogDto> GetCatalogAsync(CancellationToken ct = default);
        /// <summary>Price a cart for delivery to a city (fee, total, weight) without placing it.</summary>
        Task<ShopQuoteDto> QuoteAsync(ShopQuoteRequest req, CancellationToken ct = default);

        // ── Customer ─────────────────────────────────────────────────────
        Task<CustomerAuthResponse> RegisterAsync(CustomerRegisterRequest req, CancellationToken ct = default);
        Task<CustomerAuthResponse> LoginAsync(CustomerLoginRequest req, CancellationToken ct = default);
        Task<CustomerDto?> MeAsync(int userId, CancellationToken ct = default);
        Task<PlaceOrderResult> PlaceOrderAsync(int userId, PlaceOrderRequest req, CancellationToken ct = default);
        Task<List<OnlineOrderDto>> MyOrdersAsync(int userId, CancellationToken ct = default);
        Task<OnlineOrderDto?> GetMyOrderAsync(int userId, string code, CancellationToken ct = default);
        Task<bool> CustomerCancelAsync(int userId, string code, CancellationToken ct = default);

        // ── Till / admin ─────────────────────────────────────────────────
        Task<OnlineOrderInboxDto> InboxAsync(bool includeDone, CancellationToken ct = default);
        Task<OnlineOrderDto?> GetAsync(int id, CancellationToken ct = default);
        /// <summary>Accept a pay-at-pickup order: creates the open invoice at the till (kitchen ticket prints).</summary>
        Task<(bool ok, string? error, OnlineOrderDto? order)> AcceptAsync(int id, string actor, CancellationToken ct = default);
        Task<(bool ok, string? error, OnlineOrderDto? order)> SetStatusAsync(int id, string status, string actor, string? reason, CancellationToken ct = default);

        // ── Called by the payments module when a MontyPay order settles ──
        Task MarkPaidAsync(int orderId, int onlinePaymentId, CancellationToken ct = default);
    }
}
