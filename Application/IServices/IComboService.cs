using Application.DTOs;

namespace Application.IServices
{
    public interface IComboService
    {
        Task<List<ComboDto>> ListAsync(bool includeInactive = false, CancellationToken ct = default);
        Task<ComboDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<ComboDto>> CreateAsync(ComboUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<ComboDto>> UpdateAsync(int id, ComboUpsertDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// Adds the combo's items to an open invoice (or opens a new one) through
        /// the existing order path, then books combo price − items total when the
        /// combo is cheaper.
        /// </summary>
        Task<BaseResponse<ComboSellResultDto>> SellAsync(ComboSellDto dto, CancellationToken ct = default);
    }
}
