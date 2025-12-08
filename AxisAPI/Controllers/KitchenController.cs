using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KitchenController : ControllerBase
    {
        private readonly IKitchenService _kitchenService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public KitchenController(
            IKitchenService kitchenService,
            IHttpContextAccessor httpContextAccessor)
        {
            _kitchenService = kitchenService;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Get all kitchen orders for Chef
        /// Shows FNB orders (excludes TCG Retail)
        /// </summary>
        /// <param name="foodStatusId">Optional: Filter by food status (7=Pending, 8=InProgress, 9=Ready, 10=Served)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>List of kitchen orders</returns>
        [HttpGet("orders")]
        [Authorize(Roles = "admin,chef,cashier")]
        public async Task<ActionResult<List<KitchenOrderDto>>> GetKitchenOrders(
            [FromQuery] int? foodStatusId,
            CancellationToken ct)
        {
            var orders = await _kitchenService.GetKitchenOrdersAsync(foodStatusId, ct);
            return Ok(orders);
        }

        /// <summary>
        /// Get a specific kitchen order by ID
        /// </summary>
        /// <param name="transactionId">Transaction ID</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Kitchen order details</returns>
        [HttpGet("orders/{transactionId}")]
        [Authorize(Roles = "admin,chef,cashier")]
        public async Task<ActionResult<KitchenOrderDto>> GetKitchenOrderById(
            [FromRoute] int transactionId,
            CancellationToken ct)
        {
            var order = await _kitchenService.GetKitchenOrderByIdAsync(transactionId, ct);

            if (order == null)
            {
                return NotFound(new { message = "Kitchen order not found" });
            }

            return Ok(order);
        }

        /// <summary>
        /// Chef: Start preparing order (Pending -> InProgress)
        /// </summary>
        /// <param name="transactionId">Transaction ID</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Success status</returns>
        [HttpPost("orders/{transactionId}/start")]
        [Authorize(Roles = "admin,chef")]
        public async Task<ActionResult> StartPreparingOrder(
            [FromRoute] int transactionId,
            CancellationToken ct)
        {
            var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Unknown";

            // Food Status: 8 = InProgress
            var success = await _kitchenService.UpdateFoodStatusAsync(transactionId, 8, userName, ct);

            if (!success)
            {
                return NotFound(new { message = "Order not found" });
            }

            return Ok(new { message = "Order marked as in progress", transactionId });
        }

        /// <summary>
        /// Chef: Mark order as ready for pickup (InProgress -> Ready)
        /// </summary>
        /// <param name="transactionId">Transaction ID</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Success status</returns>
        [HttpPost("orders/{transactionId}/ready")]
        [Authorize(Roles = "admin,chef")]
        public async Task<ActionResult> MarkOrderReady(
            [FromRoute] int transactionId,
            CancellationToken ct)
        {
            var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Unknown";

            // Food Status: 9 = Ready
            var success = await _kitchenService.UpdateFoodStatusAsync(transactionId, 9, userName, ct);

            if (!success)
            {
                return NotFound(new { message = "Order not found" });
            }

            return Ok(new { message = "Order marked as ready", transactionId });
        }

        /// <summary>
        /// Waiter: Mark order as served to customer (Ready -> Served)
        /// </summary>
        /// <param name="transactionId">Transaction ID</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Success status</returns>
        [HttpPost("orders/{transactionId}/served")]
        [Authorize(Roles = "admin,cashier,gamecashier")]
        public async Task<ActionResult> MarkOrderServed(
            [FromRoute] int transactionId,
            CancellationToken ct)
        {
            var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Unknown";

            // Food Status: 10 = Served
            var success = await _kitchenService.UpdateFoodStatusAsync(transactionId, 10, userName, ct);

            if (!success)
            {
                return NotFound(new { message = "Order not found" });
            }

            return Ok(new { message = "Order marked as served", transactionId });
        }

        /// <summary>
        /// Update food status manually (admin only)
        /// </summary>
        /// <param name="dto">Update request</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Success status</returns>
        [HttpPut("orders/status")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult> UpdateFoodStatus(
            [FromBody] UpdateFoodStatusDto dto,
            CancellationToken ct)
        {
            var userName = _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Unknown";

            var success = await _kitchenService.UpdateFoodStatusAsync(
                dto.TransactionId,
                dto.NewFoodStatusId,
                userName,
                ct);

            if (!success)
            {
                return NotFound(new { message = "Order not found" });
            }

            return Ok(new { message = "Food status updated", dto.TransactionId, dto.NewFoodStatusId });
        }

        /// <summary>
        /// Get kitchen statistics for dashboard
        /// </summary>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Kitchen statistics</returns>
        [HttpGet("stats")]
        [Authorize(Roles = "admin,chef")]
        public async Task<ActionResult<KitchenStatsDto>> GetKitchenStats(CancellationToken ct)
        {
            var stats = await _kitchenService.GetKitchenStatsAsync(ct);
            return Ok(stats);
        }
    }
}
