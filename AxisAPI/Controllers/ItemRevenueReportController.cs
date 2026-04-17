using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/item-revenue-report")]
    //[Authorize(Roles = "admin")]
    public class ItemRevenueReportController : ControllerBase
    {
        private readonly IItemRevenueReportService _svc;

        public ItemRevenueReportController(IItemRevenueReportService svc)
        {
            _svc = svc;
        }

        /// <summary>
        /// Item revenue report with cost, profit, and stock analysis.
        /// Supports date range and multi-category filtering.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ItemRevenueReportDto>> GetReport(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] List<int>? categoryIds,
            CancellationToken ct)
        {
            var request = new ItemRevenueReportRequestDto
            {
                From = from,
                To = to,
                CategoryIds = categoryIds,
            };

            var result = await _svc.GetReportAsync(request, ct);
            return Ok(result);
        }
    }
}