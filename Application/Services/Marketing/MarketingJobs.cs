using Application.IServices;
using Microsoft.Extensions.Logging;

namespace Application.Services.Marketing
{
    /// <summary>
    /// Hangfire entry point. Register in Program.cs with:
    /// Hangfire.RecurringJob.AddOrUpdate&lt;Application.Services.Marketing.MarketingJobs&gt;("marketing-send-due-campaigns",
    ///     j => j.SendDueCampaignsAsync(CancellationToken.None), "*/5 * * * *");
    /// </summary>
    public class MarketingJobs
    {
        private readonly IMarketingCampaignService _campaigns;
        private readonly ILogger<MarketingJobs> _logger;

        public MarketingJobs(IMarketingCampaignService campaigns, ILogger<MarketingJobs> logger)
        {
            _campaigns = campaigns; _logger = logger;
        }

        public async Task SendDueCampaignsAsync(CancellationToken ct)
        {
            try
            {
                var n = await _campaigns.SendDueAsync(DateTime.UtcNow, ct);
                if (n > 0) _logger.LogInformation("Marketing: {Count} scheduled campaign(s) sent", n);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Marketing due-campaigns job failed");
            }
        }
    }
}
