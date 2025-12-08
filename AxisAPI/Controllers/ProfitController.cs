using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProfitController : ControllerBase
    {
        private readonly IProfitService _profitService;

        public ProfitController(IProfitService profitService)
        {
            _profitService = profitService;
        }

        /// <summary>
        /// Calculate FNB (Food & Beverage) profit for a given period
        /// Excludes TCG Retail items
        /// </summary>
        /// <param name="from">Start date (inclusive)</param>
        /// <param name="to">End date (inclusive)</param>
        /// <param name="categoryIds">Optional comma-separated list of item category IDs to filter by</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>FNB profit details including revenue, expenses, net profit, and profit margin</returns>
        [HttpGet("fnb")]
        [Authorize(Roles = "admin,admin_fnb")]
        public async Task<ActionResult<ProfitDto>> GetFnbProfit(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? categoryIds,
            CancellationToken ct)
        {
            var result = await _profitService.CalculateFnbProfitAsync(from, to, categoryIds, ct);
            return Ok(result);
        }

        /// <summary>
        /// Calculate Gaming profit for a given period (game sessions only)
        /// </summary>
        /// <param name="from">Start date (inclusive)</param>
        /// <param name="to">End date (inclusive)</param>
        /// <param name="categoryIds">Optional comma-separated list of game category IDs to filter by</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Gaming profit details including revenue, expenses, net profit, and profit margin</returns>
        [HttpGet("gaming")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<ProfitDto>> GetGamingProfit(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? categoryIds,
            CancellationToken ct)
        {
            var result = await _profitService.CalculateGamingProfitAsync(from, to, categoryIds, ct);
            return Ok(result);
        }

        /// <summary>
        /// Calculate TCG Retail profit for a given period
        /// </summary>
        /// <param name="from">Start date (inclusive)</param>
        /// <param name="to">End date (inclusive)</param>
        /// <param name="categoryIds">Optional comma-separated list of TCG category IDs to filter by</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>TCG Retail profit details including revenue, expenses, net profit, and profit margin</returns>
        [HttpGet("tcg-retail")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<ProfitDto>> GetTcgRetailProfit(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] string? categoryIds,
            CancellationToken ct)
        {
            var result = await _profitService.CalculateTcgRetailProfitAsync(from, to, categoryIds, ct);
            return Ok(result);
        }

        /// <summary>
        /// Calculate overall business profit with detailed breakdown (FNB + Gaming + TCG Retail)
        /// </summary>
        /// <param name="from">Start date (inclusive)</param>
        /// <param name="to">End date (inclusive)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Overall profit breakdown showing FNB, Gaming, TCG Retail, and combined totals</returns>
        [HttpGet("overall")]
        [Authorize(Roles = "admin")]
        public async Task<ActionResult<DetailedOverallProfitDto>> GetOverallProfit(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            CancellationToken ct)
        {
            var result = await _profitService.CalculateOverallProfitAsync(from, to, ct);
            return Ok(result);
        }
    }
}
