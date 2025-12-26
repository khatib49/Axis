using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LoyaltyController : ControllerBase
    {
        private readonly ILoyaltyService _loyaltyService;
        private readonly ILogger<LoyaltyController> _logger;

        public LoyaltyController(ILoyaltyService loyaltyService, ILogger<LoyaltyController> logger)
        {
            _loyaltyService = loyaltyService;
            _logger = logger;
        }

        /// <summary>
        /// Calculate and assign tickets immediately when transaction closes
        /// Called automatically by your POS system when transaction is completed
        /// </summary>
        [HttpPost("calculate-tickets")]
        public async Task<ActionResult<CalculateTicketsResponse>> CalculateTickets([FromBody] CalculateTicketsRequest request)
        {
            if (request == null)
            {
                return BadRequest(new CalculateTicketsResponse
                {
                    Success = false,
                    Message = "Invalid request"
                });
            }

            var response = await _loyaltyService.CalculateAndAssignTicketsAsync(request);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// Get customer's current month ticket balance
        /// </summary>
        [HttpGet("customer/{phone}/balance")]
        public async Task<ActionResult<CustomerBalanceResponse>> GetCustomerBalance(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return BadRequest("Phone number is required");
            }

            var balance = await _loyaltyService.GetCustomerBalanceAsync(phone);

            if (balance == null)
            {
                return NotFound("Customer not found");
            }

            return Ok(balance);
        }

        /// <summary>
        /// Get weekly leaderboard (current week)
        /// </summary>
        [HttpGet("leaderboard/weekly")]
        public async Task<ActionResult<List<LeaderboardEntryDTO>>> GetWeeklyLeaderboard()
        {
            var leaderboard = await _loyaltyService.GetWeeklyLeaderboardAsync();
            return Ok(leaderboard);
        }

        /// <summary>
        /// Get monthly leaderboard (current month)
        /// </summary>
        [HttpGet("leaderboard/monthly")]
        public async Task<ActionResult<List<LeaderboardEntryDTO>>> GetMonthlyLeaderboard()
        {
            var leaderboard = await _loyaltyService.GetMonthlyLeaderboardAsync();
            return Ok(leaderboard);
        }

        /// <summary>
        /// ADMIN: Conduct weekly draw on-demand
        /// Click the button in your admin panel to trigger this
        /// </summary>
        [HttpPost("admin/draw/weekly")]
        public async Task<ActionResult<DrawResponse>> ConductWeeklyDraw([FromBody] DrawRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PrizeName))
            {
                return BadRequest(new DrawResponse
                {
                    Success = false,
                    Message = "Prize name is required"
                });
            }

            _logger.LogInformation($"Admin triggering weekly draw for prize: {request.PrizeName}");

            var response = await _loyaltyService.ConductWeeklyDrawAsync(request);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// ADMIN: Conduct monthly draw on-demand
        /// Click the button in your admin panel to trigger this
        /// </summary>
        [HttpPost("admin/draw/monthly")]
        public async Task<ActionResult<DrawResponse>> ConductMonthlyDraw([FromBody] DrawRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.PrizeName))
            {
                return BadRequest(new DrawResponse
                {
                    Success = false,
                    Message = "Prize name is required"
                });
            }

            _logger.LogInformation($"Admin triggering monthly draw for prize: {request.PrizeName}");

            var response = await _loyaltyService.ConductMonthlyDrawAsync(request);

            if (!response.Success)
            {
                return BadRequest(response);
            }

            return Ok(response);
        }

        /// <summary>
        /// ADMIN: Manually invalidate expired tickets (usually done automatically at month end)
        /// </summary>
        [HttpPost("admin/invalidate-expired")]
        public async Task<ActionResult> InvalidateExpiredTickets()
        {
            _logger.LogInformation("Admin triggering ticket expiration");
            await _loyaltyService.InvalidateExpiredTicketsAsync();
            return Ok(new { Message = "Expired tickets invalidated successfully" });
        }

        /// <summary>
        /// ADMIN: Get all unclaimed prizes
        /// </summary>
        [HttpGet("admin/unclaimed-prizes")]
        public async Task<ActionResult> GetUnclaimedPrizes()
        {
            // This would require additional repository methods if you want it
            // For now, just a placeholder
            return Ok(new { Message = "Feature coming soon" });
        }

        /// <summary>
        /// ADMIN: Mark a prize as claimed
        /// </summary>
        [HttpPost("admin/claim-prize")]
        public async Task<ActionResult> ClaimPrize([FromBody] ClaimPrizeRequest request)
        {
            // This would require additional repository methods if you want it
            // For now, just a placeholder
            return Ok(new { Message = "Feature coming soon" });
        }
    }

    // DTO for claiming prizes
    public class ClaimPrizeRequest
    {
        public string DrawType { get; set; } = string.Empty; // "weekly" or "monthly"
        public int WinnerId { get; set; }
    }
}
