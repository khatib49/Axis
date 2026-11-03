using Application.DTOs;

namespace Application.IServices
{
    public interface IHrTipService
    {
        Task<List<HrTipPoolDto>> ListAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
        Task<HrTipPoolDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<HrTipPoolDto>> CreateAsync(HrTipPoolCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<HrTipPoolDto>> UpdateAsync(int id, HrTipPoolUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<HrTipPoolDto>> ComputeAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<HrTipPoolDto>> DistributeAsync(int id, CancellationToken ct = default);
        Task<HrTipReportDto> ReportAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    }
}
