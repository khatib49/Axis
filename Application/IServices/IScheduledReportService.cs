using Application.DTOs;

namespace Application.IServices
{
    public interface IScheduledReportService
    {
        Task<IReadOnlyList<ScheduledReportDto>> ListAsync(CancellationToken ct = default);
        Task<ScheduledReportDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<ScheduledReportDto>> CreateAsync(ScheduledReportCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<ScheduledReportDto>> UpdateAsync(int id, ScheduledReportUpdateDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(int id, CancellationToken ct = default);
        Task<IReadOnlyList<ReportRunDto>> GetRunsAsync(int id, int take = 50, CancellationToken ct = default);

        /// <summary>Build + send one report now. day = null → today.</summary>
        Task<BaseResponse<ReportRunDto>> RunNowAsync(int id, DateTime? day, CancellationToken ct = default);
        /// <summary>Preview without sending.</summary>
        Task<BaseResponse<ReportPreviewDto>> PreviewAsync(int id, DateTime? day, CancellationToken ct = default);
        /// <summary>Called hourly by Hangfire: sends every active report due in nowUtc's hour.</summary>
        Task<int> RunDueAsync(DateTime nowUtc, CancellationToken ct = default);
    }
}
