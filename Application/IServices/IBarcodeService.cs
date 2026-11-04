using Application.DTOs;

namespace Application.IServices
{
    public interface IBarcodeService
    {
        Task<List<BarcodeDto>> ListAsync(int? itemId, string? search, CancellationToken ct = default);
        Task<BarcodeDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<BarcodeDto>> CreateAsync(BarcodeCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<BarcodeDto>> UpdateAsync(int id, BarcodeUpdateDto dto, CancellationToken ct = default);
        Task<BaseResponse<BarcodeDto>> SetPrimaryAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteAsync(int id, CancellationToken ct = default);
        /// <summary>Till scan: exact match on the normalised code, variant codes first, then item codes, then variant SKU, then an in-stock serial.</summary>
        Task<BaseResponse<BarcodeLookupDto>> LookupAsync(string code, CancellationToken ct = default);
        /// <summary>Creates the in-house EAN-13 for the item/variant; becomes primary when none exists.</summary>
        Task<BaseResponse<BarcodeDto>> GenerateAsync(BarcodeGenerateDto dto, CancellationToken ct = default);
        /// <summary>One label per item and per active variant for the label page.</summary>
        Task<List<LabelDto>> LabelsAsync(IEnumerable<int> itemIds, CancellationToken ct = default);
    }
}
