using Application.DTOs;

namespace Application.IServices
{
    public interface ISupplierService
    {
        Task<IReadOnlyList<SupplierDto>> ListAsync(bool includeHidden, CancellationToken ct = default);
        Task<SupplierDto?> GetAsync(int id, CancellationToken ct = default);
        Task<SupplierDto> CreateAsync(SupplierCreateDto dto, CancellationToken ct = default);
        Task<SupplierDto> UpdateAsync(int id, SupplierUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeactivateAsync(int id, CancellationToken ct = default);
    }
}
