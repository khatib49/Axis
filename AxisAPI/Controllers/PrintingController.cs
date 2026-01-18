using Application.DTOs;
using Application.IServices;
using Application.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PrintingController : ControllerBase
    {
        private readonly IReceiptPrintingService _printingService;
        private readonly IKitchenBarOrderService _orderService;
        private readonly ILogger<PrintingController> _logger;

        public PrintingController(
            IReceiptPrintingService printingService,
            IKitchenBarOrderService orderService,
            ILogger<PrintingController> logger)
        {
            _printingService = printingService;
            _orderService = orderService;
            _logger = logger;
        }

        /// <summary>
        /// Generate receipt for a kitchen/bar order (returns raw ESC/POS bytes)
        /// </summary>
        [HttpPost("kitchen-bar-receipt/{orderId}")]
        [ProducesResponseType(typeof(FileContentResult), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> GenerateKitchenBarReceipt(
    int orderId,
    [FromQuery] bool includeOtherItems = false,
    CancellationToken ct = default)
        {
            var order = await _orderService.GetByIdAsync(orderId, ct);
            if (order == null)
                return NotFound(new { message = "Order not found" });

            List<KitchenBarOrderDto>? otherOrders = null;

            // Optionally include other items from same transaction
            if (includeOtherItems)
            {
                var allOrders = await _orderService.ListAsync(
                    new KitchenBarOrderListDto { Page = 1, PageSize = 100 }, ct);

                otherOrders = allOrders.Data  // FIXED: Changed from .Items to .Data
                    .Where(o => o.TransactionId == order.TransactionId
                             && o.Id != orderId
                             && o.Station == order.Station)
                    .ToList();
            }

            var receiptBytes = _printingService.GenerateKitchenBarReceipt(order, otherOrders);

            // Mark as printed
            await _orderService.MarkAsPrintedAsync(new List<int> { orderId }, ct);

            _logger.LogInformation(
                "Generated receipt for order {OrderId} in {Station}",
                orderId, order.Station);

            return File(receiptBytes, "application/octet-stream", $"receipt-{orderId}.bin");
        }


        /// <summary>
        /// Generate preview text for a kitchen/bar order (for testing)
        /// </summary>
        [HttpGet("kitchen-bar-preview/{orderId}")]
        [ProducesResponseType(typeof(string), 200)]
        [ProducesResponseType(404)]
        public async Task<IActionResult> PreviewKitchenBarReceipt(
    int orderId,
    [FromQuery] bool includeOtherItems = false,
    CancellationToken ct = default)
        {
            var order = await _orderService.GetByIdAsync(orderId, ct);
            if (order == null)
                return NotFound(new { message = "Order not found" });

            List<KitchenBarOrderDto>? otherOrders = null;

            if (includeOtherItems)
            {
                var allOrders = await _orderService.ListAsync(
                    new KitchenBarOrderListDto { Page = 1, PageSize = 100 }, ct);

                otherOrders = allOrders.Data  // FIXED: Changed from .Items to .Data
                    .Where(o => o.TransactionId == order.TransactionId
                             && o.Id != orderId
                             && o.Station == order.Station)
                    .ToList();
            }

            var receiptText = _printingService.GenerateReceiptText(order, otherOrders);

            return Ok(new { receipt = receiptText });
        }



        /// <summary>
        /// Batch print multiple orders
        /// </summary>
        [HttpPost("batch-print")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        public async Task<IActionResult> BatchPrint(
            [FromBody] BatchPrintRequest request,
            CancellationToken ct = default)
        {
            if (request.OrderIds == null || !request.OrderIds.Any())
                return BadRequest(new { message = "OrderIds are required" });

            var receipts = new List<byte[]>();

            foreach (var orderId in request.OrderIds)
            {
                var order = await _orderService.GetByIdAsync(orderId, ct);
                if (order != null)
                {
                    var receiptBytes = _printingService.GenerateKitchenBarReceipt(order, null);
                    receipts.Add(receiptBytes);
                }
            }

            // Mark all as printed
            await _orderService.MarkAsPrintedAsync(request.OrderIds, ct);

            // Combine all receipts
            var combinedReceipt = receipts.SelectMany(r => r).ToArray();

            _logger.LogInformation(
                "Batch printed {Count} receipts",
                receipts.Count);

            return File(combinedReceipt, "application/octet-stream", "batch-receipts.bin");
        }

        public class BatchPrintRequest
        {
            public List<int> OrderIds { get; set; } = new();
        }
    }
    }
