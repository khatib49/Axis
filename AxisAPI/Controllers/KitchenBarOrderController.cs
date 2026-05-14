using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KitchenBarOrderController : ControllerBase
    {
        private readonly IKitchenBarOrderService _service;
        private readonly ILogger<KitchenBarOrderController> _logger;

        public KitchenBarOrderController(
            IKitchenBarOrderService service,
            ILogger<KitchenBarOrderController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// Get a single kitchen/bar order by ID
        /// </summary>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(KitchenBarOrderDto), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GetById(int id, CancellationToken ct)
        {
            var order = await _service.GetByIdAsync(id, ct);
            if (order == null)
                return NotFound(new { message = "Order not found" });

            return Ok(order);
        }

        /// <summary>
        /// Get paginated list of kitchen/bar orders with filters
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(PaginatedResponse<KitchenBarOrderDto>), 200)]
        public async Task<IActionResult> List(
            [FromQuery] string? station = null,
            [FromQuery] string? status = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            var filter = new KitchenBarOrderListDto(
                station, status, fromDate, toDate, page, pageSize);

            var result = await _service.ListAsync(filter, ct);
            return Ok(result);
        }

        /// <summary>
        /// Get pending orders for Kitchen station
        /// Roles: Chef, Admin
        /// </summary>
        [HttpGet("kitchen/pending")]
        //[Authorize(Roles = "Chef,Admin")]
        [ProducesResponseType(typeof(List<KitchenBarOrderDto>), 200)]
        public async Task<IActionResult> GetKitchenPending(CancellationToken ct)
        {
            var orders = await _service.GetPendingOrdersByStationAsync("Kitchen", ct);
            return Ok(orders);
        }

        /// <summary>
        /// Get pending orders for Bar station
        /// Roles: Bartender, Admin
        /// </summary>
        [HttpGet("bar/pending")]
        //[Authorize(Roles = "Bartender,Admin")]
        [ProducesResponseType(typeof(List<KitchenBarOrderDto>), 200)]
        public async Task<IActionResult> GetBarPending(CancellationToken ct)
        {
            var orders = await _service.GetPendingOrdersByStationAsync("Bar", ct);
            return Ok(orders);
        }

        /// <summary>
        /// Update order status (Pending -> Preparing -> Done)
        /// </summary>
        [HttpPatch("{id}/status")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> UpdateStatus(
            int id,
            [FromBody] UpdateStatusRequest request,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(request.Status))
                return BadRequest(new { message = "Status is required" });

            var validStatuses = new[] { "Pending", "Preparing", "Done" };
            if (!validStatuses.Contains(request.Status))
                return BadRequest(new { message = $"Invalid status. Valid values: {string.Join(", ", validStatuses)}" });

            // Get current user ID for PreparedBy
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            int? preparedBy = userIdClaim != null ? int.Parse(userIdClaim.Value) : null;

            var dto = new KitchenBarOrderUpdateStatusDto(
                id,
                request.Status,
                preparedBy);

            var success = await _service.UpdateStatusAsync(dto, ct);
            if (!success)
                return NotFound(new { message = "Order not found" });

            _logger.LogInformation(
                "User {Username} updated order {OrderId} to status {Status}",
                User.Identity?.Name, id, request.Status);

            return Ok(new { message = "Status updated successfully" });
        }

        /// <summary>
        /// Mark orders as printed (updates PrintedAt timestamp)
        /// </summary>
        [HttpPost("mark-printed")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> MarkAsPrinted(
            [FromBody] MarkPrintedRequest request,
            CancellationToken ct)
        {
            if (request.OrderIds == null || !request.OrderIds.Any())
                return BadRequest(new { message = "OrderIds are required" });

            var success = await _service.MarkAsPrintedAsync(request.OrderIds, ct);
            if (!success)
                return BadRequest(new { message = "No orders were marked as printed" });

            return Ok(new
            {
                message = "Orders marked as printed successfully",
                count = request.OrderIds.Count
            });
        }

        /// <summary>
        /// Get orders grouped by station with counts
        /// </summary>
        [HttpGet("summary")]
        [ProducesResponseType(typeof(StationSummaryResponse), 200)]
        public async Task<IActionResult> GetSummary(CancellationToken ct)
        {
            var kitchenPending = await _service.GetPendingOrdersByStationAsync("Kitchen", ct);
            var barPending = await _service.GetPendingOrdersByStationAsync("Bar", ct);

            var summary = new StationSummaryResponse
            {
                Kitchen = new StationInfo
                {
                    PendingCount = kitchenPending.Count,
                    Orders = kitchenPending
                },
                Bar = new StationInfo
                {
                    PendingCount = barPending.Count,
                    Orders = barPending
                }
            };

            return Ok(summary);
        }

        // Request/Response models
        public class UpdateStatusRequest
        {
            public string Status { get; set; } = string.Empty;
        }

        public class MarkPrintedRequest
        {
            public List<int> OrderIds { get; set; } = new();
        }

        public class StationSummaryResponse
        {
            public StationInfo Kitchen { get; set; } = new();
            public StationInfo Bar { get; set; } = new();
        }

        public class StationInfo
        {
            public int PendingCount { get; set; }
            public List<KitchenBarOrderDto> Orders { get; set; } = new();
        }
    }
}
