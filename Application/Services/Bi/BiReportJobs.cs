using Application.IServices;
using Microsoft.Extensions.Logging;

namespace Application.Services.Bi
{
    /// <summary>
    /// Hangfire entry point. Register in Program.cs with:
    /// Hangfire.RecurringJob.AddOrUpdate&lt;Application.Services.Bi.BiReportJobs&gt;("bi-scheduled-reports",
    ///     j => j.RunDueReportsAsync(CancellationToken.None), "5 * * * *");
    /// </summary>
    public class BiReportJobs
    {
        private readonly IScheduledReportService _reports;
        private readonly ILogger<BiReportJobs> _logger;

        public BiReportJobs(IScheduledReportService reports, ILogger<BiReportJobs> logger)
        {
            _reports = reports;
            _logger = logger;
        }

        public async Task RunDueReportsAsync(CancellationToken ct)
        {
            try
            {
                var n = await _reports.RunDueAsync(DateTime.UtcNow, ct);
                _logger.LogInformation("BI scheduled reports: {Count} report(s) processed", n);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BI scheduled reports job failed");
            }
        }
    }
}
