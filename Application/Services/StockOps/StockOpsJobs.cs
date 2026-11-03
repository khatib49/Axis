using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.StockOps
{
    /// <summary>
    /// Hangfire jobs for the StockOps module. Registered by Program.cs:
    ///   RecurringJob.AddOrUpdate&lt;StockOpsJobs&gt;("stockops-expiry-alert", j => j.ExpiryAlertAsync(), "0 7 * * *", new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
    /// </summary>
    public class StockOpsJobs
    {
        public const string NotificationType = "StockExpiry";
        public const int AlertDays = 3;

        private readonly IStockBatchService _batches;
        private readonly INotificationService _notifications;
        private readonly UserManager<AppUser> _users;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<StockOpsJobs> _logger;

        public StockOpsJobs(
            IStockBatchService batches,
            INotificationService notifications,
            UserManager<AppUser> users,
            ApplicationDbContext db,
            ILogger<StockOpsJobs> logger)
        {
            _batches = batches; _notifications = notifications; _users = users; _db = db; _logger = logger;
        }

        /// <summary>
        /// Writes one Notification per admin / stock user listing batches
        /// that expire within the next 3 days (already-expired, unwritten-off
        /// batches are included). Skips users already notified today.
        /// </summary>
        public async Task ExpiryAlertAsync()
        {
            try
            {
                var expiring = await _batches.GetExpiringAsync(AlertDays);
                var expired = await _batches.GetExpiredAsync();
                var all = expired.Concat(expiring).ToList();
                if (all.Count == 0)
                {
                    _logger.LogInformation("StockOps expiry alert: nothing expiring within {Days} days", AlertDays);
                    return;
                }

                var lines = all.Select(b =>
                {
                    var when = b.IsExpired
                        ? $"expired {Math.Abs(b.DaysToExpiry ?? 0)}d ago"
                        : b.DaysToExpiry == 0 ? "expires today" : $"expires in {b.DaysToExpiry}d";
                    var lot = string.IsNullOrWhiteSpace(b.LotNumber) ? $"batch #{b.Id}" : $"lot {b.LotNumber}";
                    return $"- {b.IngredientName}: {b.RemainingQuantity:0.###} {b.Unit} ({lot}) {when}";
                });
                var body = $"{all.Count} batch(es) need attention:\n" + string.Join("\n", lines);
                var title = expired.Count > 0
                    ? $"Stock expiry: {expired.Count} expired, {expiring.Count} expiring soon"
                    : $"Stock expiry: {expiring.Count} batch(es) expiring within {AlertDays} days";

                var recipients = new Dictionary<int, AppUser>();
                foreach (var role in new[] { "admin", "stock", "chef" })
                {
                    foreach (var u in await _users.GetUsersInRoleAsync(role))
                        if (u.DeletedAt == null) recipients[u.Id] = u;
                }

                var today = DateTime.UtcNow.Date;
                var alreadyNotified = await _db.Set<Notification>().AsNoTracking()
                    .Where(n => n.Type == NotificationType && n.CreatedOn >= today)
                    .Select(n => n.UserId)
                    .Distinct()
                    .ToListAsync();
                var skip = alreadyNotified.ToHashSet();

                var sent = 0;
                foreach (var user in recipients.Values)
                {
                    if (skip.Contains(user.Id)) continue;
                    await _notifications.CreateAsync(new NotificationCreateDto(user.Id, title, body, NotificationType));
                    sent++;
                }

                _logger.LogInformation("StockOps expiry alert: {Batches} batches, {Sent} notifications written", all.Count, sent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StockOps expiry alert job failed");
            }
        }
    }
}
