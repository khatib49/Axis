using Application.DTOs;

namespace Application.IServices
{
    public interface IPurchaseService
    {
        // Creates a Purchase + lines + StockMovements all atomically.
        // For each line: bumps Ingredient.QuantityOnHand and updates
        // Ingredient.BuyPricePerUnit to the latest cost.
        Task<PurchaseDto> CreateAsync(PurchaseCreateDto dto, string? actor, CancellationToken ct = default);

        Task<PurchaseDto?> GetAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<PurchaseDto>> ListAsync(PurchaseFilterDto filter, CancellationToken ct = default);

        // Per-ingredient cost history for the price-trend chart.
        Task<IReadOnlyList<PriceTrendPointDto>> GetPriceTrendAsync(int ingredientId, DateTime? from, DateTime? to, CancellationToken ct = default);
    }
}
