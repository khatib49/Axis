using Application.DTOs;

namespace Application.IServices
{
    public interface IMarketingCampaignService
    {
        Task<IReadOnlyList<CampaignDto>> ListAsync(string? status = null, CancellationToken ct = default);
        Task<CampaignDto?> GetAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<CampaignDto>> CreateAsync(CampaignCreateDto dto, CancellationToken ct = default);
        Task<BaseResponse<CampaignDto>> UpdateAsync(int id, CampaignUpdateDto dto, CancellationToken ct = default);
        Task<BaseResponse<bool>> DeleteAsync(int id, CancellationToken ct = default);
        Task<BaseResponse<CampaignDto>> ScheduleAsync(int id, DateTime scheduledFor, CancellationToken ct = default);
        Task<BaseResponse<CampaignDto>> CancelAsync(int id, CancellationToken ct = default);
        /// <summary>Builds recipients from the segment and sends them all. Never throws.</summary>
        Task<BaseResponse<CampaignSendSummaryDto>> SendNowAsync(int id, CancellationToken ct = default);
        /// <summary>Sends one rendered message to one address; records an IsTest recipient row.</summary>
        Task<BaseResponse<CampaignTestSendResultDto>> TestSendAsync(int id, string address, CancellationToken ct = default);
        Task<BaseResponse<CampaignStatsDto>> StatsAsync(int id, CancellationToken ct = default);
        Task<PaginatedResponse<CampaignRecipientDto>> RecipientsAsync(int id, string? status, string? search, int page, int pageSize, CancellationToken ct = default);
        /// <summary>Called every 5 minutes by Hangfire: sends every Scheduled campaign whose ScheduledFor &lt;= nowUtc.</summary>
        Task<int> SendDueAsync(DateTime nowUtc, CancellationToken ct = default);
    }
}
