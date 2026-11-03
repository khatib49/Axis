using Application.IServices;
using Microsoft.Extensions.Logging;

namespace Application.Services.Floor
{
    /// <summary>
    /// Hangfire entry point for the daily reservation reminders.
    ///
    /// Wired in Program.cs (daily 10:00 Beirut = 07:00 UTC):
    ///   Hangfire.RecurringJob.AddOrUpdate&lt;Application.Services.Floor.ReservationReminderJobs&gt;(
    ///       "reservation-reminders",
    ///       j => j.SendTomorrowRemindersAsync(CancellationToken.None),
    ///       "0 7 * * *");
    /// </summary>
    public class ReservationReminderJobs
    {
        private readonly IReservationNotificationService _notifications;
        private readonly ILogger<ReservationReminderJobs> _logger;

        public ReservationReminderJobs(IReservationNotificationService notifications, ILogger<ReservationReminderJobs> logger)
        {
            _notifications = notifications;
            _logger = logger;
        }

        public async Task SendTomorrowRemindersAsync(CancellationToken ct)
        {
            var r = await _notifications.SendTomorrowRemindersAsync(null, ct);
            if (!r.TemplateConfigured) return; // already logged by the service
            _logger.LogInformation("Reservation reminder job: {Sent}/{Candidates} sent, {Failed} failed, {NoPhone} skipped (no phone)",
                r.Sent, r.Candidates, r.Failed, r.SkippedNoPhone);
        }
    }
}
