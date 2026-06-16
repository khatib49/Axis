using Application.DTOs;

namespace Application.IServices
{
    public interface IIngredientService
    {
        Task<IReadOnlyList<IngredientDto>> ListAsync(bool includeHidden, CancellationToken ct = default);
        Task<IngredientDto?> GetAsync(int id, CancellationToken ct = default);
        Task<IngredientDto> CreateAsync(IngredientCreateDto dto, string? actor, CancellationToken ct = default);
        Task<IngredientDto> UpdateAsync(int id, IngredientUpdateDto dto, string? actor, CancellationToken ct = default);
        // Soft-hide. Historical recipes / stock movements keep their FK so
        // the audit trail stays intact; the chef just no longer sees this
        // ingredient on the active list.
        Task<bool> DeactivateAsync(int id, string? actor, CancellationToken ct = default);

        // Stock events — each writes one StockMovement and updates QuantityOnHand.
        Task<IngredientDto> AddStockAsync(AddStockRequestDto dto, string? actor, CancellationToken ct = default);
        Task<IngredientDto> RecordWasteAsync(RecordWasteRequestDto dto, string? actor, CancellationToken ct = default);
        Task<IngredientDto> AdjustStockAsync(AdjustStockRequestDto dto, string? actor, CancellationToken ct = default);

        // Read-only audit log.
        Task<PaginatedResponse<StockMovementDto>> GetMovementsAsync(StockMovementFilterDto filter, CancellationToken ct = default);

        // Low-stock card on the chef dashboard.
        Task<IReadOnlyList<IngredientDto>> GetLowStockAsync(CancellationToken ct = default);
    }
}
