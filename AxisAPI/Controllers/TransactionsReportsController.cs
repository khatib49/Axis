using Application.DTOs;
using Application.IServices;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TransactionsReportsController : ControllerBase
    {
        private readonly ITransactionRecordService _svc;
        private readonly IUsersService _usersService;

        public TransactionsReportsController(ITransactionRecordService svc, IUsersService usersService)
        {
            _svc = svc;
            _usersService = usersService;
        }

        /// <summary>
        /// Item transactions (flattened to one row per item line), multi-filter & paginated.
        /// </summary>
        [HttpGet("item-transactions")]
        [Authorize]
        public async Task<ActionResult<PaginatedResponse<ItemTransactionLineDto>>> GetItemTransactions(
            [FromQuery] TransactionsFilterDto f, CancellationToken ct)
        {
            var result = await _svc.GetItemTransactionsWithDetailsAsync(f, ct);
            return Ok(result);
        }

        /// <summary>
        /// Game transactions (one row per transaction), multi-filter & paginated.
        /// </summary>
        [HttpGet("game-transactions")]
        [Authorize]
        public async Task<ActionResult<PaginatedResponse<GameTransactionDetailsDto>>> GetGameTransactions(
            [FromQuery] TransactionsFilterDto f, CancellationToken ct)
        {
            var result = await _svc.GetGameTransactionsWithDetailsAsync(f, ct);
            return Ok(result);
        }

        [HttpGet("orders/count")]
        public async Task<IActionResult> GetOrdersCount([FromQuery] DateTime? from, [FromQuery] DateTime? to,[FromQuery] string? categoryIds, CancellationToken ct)
        {
            var count = await _svc.GetOrdersCountAsync(from, to, categoryIds, ct);

            return Ok(new { ordersCount = count });
        }


        [HttpGet("daily-sales")]
        [Authorize(Roles = "admin,admin_fnb")]
        public async Task<ActionResult<List<DailySalesDto>>> GetDailySales( [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] string? categoryIds,  CancellationToken ct)
        {
            var data = await _svc.GetDailySalesAsync(from, to, categoryIds, ct);
            return Ok(data);
        }



        [HttpGet("total-sales")]
        [Authorize(Roles = "admin,admin_fnb")]
        public async Task<ActionResult<List<DailySalesDto>>> GetTotalSales([FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] string? categoryIds, CancellationToken ct)
        {
            var data = await _svc.GetTotalsAsync(from, to, categoryIds, ct);
            return Ok(data);
        }

        [HttpGet("clients/count")]
        public async Task<IActionResult> GetClientUsersCount(CancellationToken ct)
        {
            var count = await _usersService.CountClientUsersAsync(ct);
            return Ok(new { count });
        }
    }
}
