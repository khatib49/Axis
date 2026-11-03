using Application.DTOs;

namespace Application.IServices
{
    public interface ISupplierBillService
    {
        Task<PaginatedResponse<SupplierBillDto>> ListAsync(SupplierBillFilterDto filter, CancellationToken ct = default);
        Task<BaseResponse<SupplierBillDto>> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<SupplierBillDto>> CreateAsync(SupplierBillCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<SupplierBillDto>> UpdateDraftAsync(int id, SupplierBillUpdateDto dto, CancellationToken ct = default);
        /// <summary>Posts a Draft bill (DR expense [+ VAT input] / CR payable) and sets it Open.</summary>
        Task<BaseResponse<SupplierBillDto>> OpenAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<SupplierBillDto>> AddPaymentAsync(int id, SupplierBillPaymentCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<SupplierBillDto>> VoidAsync(int id, string? reason, CancellationToken ct = default);
        Task<BaseResponse> DeleteDraftAsync(int id, CancellationToken ct = default);

        Task<ApAgingReportDto> GetApAgingAsync(DateTime? asOf, CancellationToken ct = default);
        Task<List<DueSoonBillDto>> GetDueSoonAsync(int days, CancellationToken ct = default);
    }
}
