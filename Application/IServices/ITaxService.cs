using Application.DTOs;

namespace Application.IServices
{
    public interface ITaxService
    {
        Task<List<TaxRateDto>> ListRatesAsync(bool includeInactive, CancellationToken ct = default);
        Task<BaseResponse<TaxRateDto>> CreateRateAsync(TaxRateUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse<TaxRateDto>> UpdateRateAsync(int id, TaxRateUpsertDto dto, CancellationToken ct = default);
        Task<BaseResponse> DeleteRateAsync(int id, CancellationToken ct = default);

        /// <summary>
        /// VAT report between two dates, one row per calendar month. Output VAT comes
        /// from paid transactions (StatusId 6); input VAT from supplier bills.
        /// </summary>
        Task<BaseResponse<VatReportDto>> GetVatReportAsync(DateTime from, DateTime to, bool inclusive, CancellationToken ct = default);
    }
}
