using Application.DTOs;

namespace Application.IServices
{
    public interface IInventoryValuationService
    {
        // Snapshot of current value + movers/slow-movers in the period.
        Task<InventoryValuationDto> GetAsync(DateTime? from, DateTime? to, CancellationToken ct = default);
    }
}
