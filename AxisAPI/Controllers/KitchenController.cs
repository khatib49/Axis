using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KitchenController : ControllerBase
    {
        private readonly IKitchenService _kitchenService;

        public KitchenController(IKitchenService kitchenService)
        {
            _kitchenService = kitchenService;
        }

        /// <summary>
        /// Get all kitchen orders
        /// Chef sees only assigned food categories
        /// Bartender sees only assigned drink categories
        /// Admin/Cashier sees everything
        /// </summary>
        [HttpGet("orders")]
        [Authorize(Roles = "admin,chef,bartender,cashier")]
        public async Task<ActionResult<List<KitchenOrderDto>>> GetKitchenOrders(
            [FromQuery] int? foodStatusId,
            CancellationToken ct)
        {
            // Automatically detect user's role from token
            var userRole = GetUserRole();

            var orders = await _kitchenService.GetKitchenOrdersAsync(foodStatusId, userRole, ct);
            return Ok(orders);
        }

        /// <summary>
        /// Get a specific kitchen order by ID
        /// Filtered by user's role (based on database configuration)
        /// </summary>
        [HttpGet("orders/{transactionId}")]
        [Authorize(Roles = "admin,chef,bartender,cashier")]
        public async Task<ActionResult<KitchenOrderDto>> GetKitchenOrderById(
            [FromRoute] int transactionId,
            CancellationToken ct)
        {
            var userRole = GetUserRole();

            var order = await _kitchenService.GetKitchenOrderByIdAsync(transactionId, userRole, ct);

            if (order == null || !order.Items.Any())
            {
                return NotFound(new { message = "Kitchen order not found or no items for your role" });
            }

            return Ok(order);
        }

        /// <summary>
        /// Chef/Bartender: Start preparing order (Pending -> InProgress)
        /// </summary>
        [HttpPost("orders/{transactionId}/start")]
        [Authorize(Roles = "admin,chef,bartender")]
        public async Task<ActionResult> StartPreparingOrder(
            [FromRoute] int transactionId,
            CancellationToken ct)
        {
            var userName = User?.Identity?.Name ?? "Unknown";

            // Food Status: 8 = InProgress
            var success = await _kitchenService.UpdateFoodStatusAsync(transactionId, 12, userName, ct);

            if (!success)
            {
                return NotFound(new { message = "Order not found" });
            }

            return Ok(new { message = "Order marked as in progress", transactionId });
        }

        /// <summary>
        /// Chef/Bartender: Mark order as ready for pickup (InProgress -> Ready)
        /// </summary>
        [HttpPost("orders/{transactionId}/ready")]
        [Authorize(Roles = "admin,chef,bartender")]
        public async Task<ActionResult> MarkOrderReady(
            [FromRoute] int transactionId,
            CancellationToken ct)
        {
            var userName = User?.Identity?.Name ?? "Unknown";

            // Food Status: 9 = Ready
            var success = await _kitchenService.UpdateFoodStatusAsync(transactionId, 13, userName, ct);

            if (!success)
            {
                return NotFound(new { message = "Order not found" });
            }

            return Ok(new { message = "Order marked as ready", transactionId });
        }

        /// <summary>
        /// Waiter/Cashier: Mark order as served to customer (Ready -> Served)
        /// </summary>
        [HttpPost("orders/{transactionId}/served")]
        [Authorize(Roles = "chef,admin,cashier,gamecashier")]
        public async Task<ActionResult> MarkOrderServed(
            [FromRoute] int transactionId,
            CancellationToken ct)
        {
            var userName = User?.Identity?.Name ?? "Unknown";

            // Food Status: 10 = Served
            var success = await _kitchenService.UpdateFoodStatusAsync(transactionId, 14, userName, ct);

            if (!success)
            {
                return NotFound(new { message = "Order not found" });
            }

            return Ok(new { message = "Order marked as served", transactionId });
        }

        /// <summary>
        /// Update food status manually (admin only)
        /// </summary>
        [HttpPut("orders/status")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult> UpdateFoodStatus(
            [FromBody] UpdateFoodStatusDto dto,
            CancellationToken ct)
        {
            var userName = User?.Identity?.Name ?? "Unknown";

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
        /// Filtered by user's role
        /// </summary>
        [HttpGet("stats")]
        [Authorize(Roles = "admin,chef,bartender")]
        public async Task<ActionResult<KitchenStatsDto>> GetKitchenStats(CancellationToken ct)
        {
            var userRole = GetUserRole();

            var stats = await _kitchenService.GetKitchenStatsAsync(userRole, ct);
            return Ok(stats);
        }

        /// <summary>
        /// Helper method to get the current user's role
        /// Returns: "chef", "bartender", or null (for admin/cashier who see everything)
        /// </summary>
        private string? GetUserRole()
        {
            var roles = User?.FindAll(ClaimTypes.Role).Select(c => c.Value.ToLower()).ToList();

            if (roles == null || !roles.Any())
                return null;

            // Priority: chef > bartender > null (admin/cashier see everything)
            if (roles.Contains("chef"))
                return "chef";

            if (roles.Contains("bartender"))
                return "bartender";

            // Admin, cashier, or other roles see everything
            return null;
        }
    }
}