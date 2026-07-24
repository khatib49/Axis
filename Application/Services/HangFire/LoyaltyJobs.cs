using Application.IServices;
using Microsoft.Extensions.Logging;

namespace Application.Services.HangFire
{
    public class LoyaltyJobs
    {
        private readonly ILoyaltyService _loyaltyService;
        private readonly ILogger<LoyaltyJobs> _logger;

        public LoyaltyJobs(ILoyaltyService loyaltyService, ILogger<LoyaltyJobs> logger)
        {
            _loyaltyService = loyaltyService;
            _logger = logger;
        }

        /// <summary>
        /// Scheduled weekly draw - Fridays at 8 PM Beirut time
        /// </summary>
        public async Task RunWeeklyDrawJob()
        {
            try
            {
                _logger.LogInformation("Starting scheduled weekly draw");

                var request = new DTOs.DrawRequest
                {
                    PrizeName = "Weekly Prize" // Configure your weekly prize
                };

                var result = await _loyaltyService.ConductWeeklyDrawAsync(request);

                if (result.Success && result.Winner != null)
                {
                    _logger.LogInformation($"Weekly draw successful. Winner: {result.Winner.CustomerPhone}");
                    // TODO: Send notification to winner (SMS/WhatsApp/Email)
                }
                else
                {
                    _logger.LogWarning($"Weekly draw failed: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in weekly draw job");
            }
        }

        /// <summary>
        /// Scheduled monthly draw - First Saturday at 9 PM Beirut time
        /// </summary>
        public async Task RunMonthlyDrawJob()
        {
            try
            {
                _logger.LogInformation("Starting scheduled monthly draw");

                var request = new DTOs.DrawRequest
                {
                    PrizeName = "iPhone 17" // Your grand prize
                };

                var result = await _loyaltyService.ConductMonthlyDrawAsync(request);

                if (result.Success && result.Winner != null)
                {
                    _logger.LogInformation($"Monthly draw successful. Winner: {result.Winner.CustomerPhone}");
                    // TODO: Send notification to winner (SMS/WhatsApp/Email)
                }
                else
                {
                    _logger.LogWarning($"Monthly draw failed: {result.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in monthly draw job");
            }
        }

        /// <summary>
        /// Daily maintenance job - Invalidate expired tickets
        /// Runs at 1 AM Beirut time
        /// </summary>
        public async Task RunTicketMaintenanceJob()
        {
            try
            {
                _logger.LogInformation("Starting ticket maintenance job");
                await _loyaltyService.InvalidateExpiredTicketsAsync();
                _logger.LogInformation("Ticket maintenance completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ticket maintenance job");
            }
        }
    }
}
