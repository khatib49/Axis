using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.OrderingChannels
{
    /// <summary>Till: delivery & takeaway board, order creation, driver assignment, status workflow.</summary>
    [ApiController]
    [Route("api/delivery/orders")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class DeliveryOrdersController : ControllerBase
    {
        private readonly IDeliveryService _svc;
        public DeliveryOrdersController(IDeliveryService svc) { _svc = svc; }

        private IActionResult FromResponse<T>(BaseResponse<T> r)
        {
            if (r.Success) return Ok(r);
            return r.Error == "NotFound" ? NotFound(r) : BadRequest(r);
        }

        /// <summary>Creates the open invoice (CreateCoffeeShopOrder, isOpenInvoice=true) and the delivery wrapper.</summary>
        [HttpPost]
        public async Task<IActionResult> Create([FromBody] DeliveryOrderCreateDto dto, CancellationToken ct)
            => FromResponse(await _svc.CreateAsync(dto, ct));

        /// <summary>Kanban data: active orders by status plus today's delivered / cancelled.</summary>
        [HttpGet("board")]
        public async Task<IActionResult> Board(CancellationToken ct) => Ok(await _svc.GetBoardAsync(ct));

        [HttpGet("stats/today")]
        public async Task<IActionResult> StatsToday(CancellationToken ct) => Ok(await _svc.GetTodayStatsAsync(ct));

        /// <summary>Orders assigned to the driver linked (Driver.UserId) to the caller. Any staff.</summary>
        [HttpGet("mine")]
        [Authorize]
        public async Task<IActionResult> Mine(CancellationToken ct) => Ok(await _svc.MyOrdersAsync(ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var o = await _svc.GetAsync(id, ct);
            return o is null ? NotFound() : Ok(o);
        }

        [HttpPost("{id:int}/assign")]
        public async Task<IActionResult> Assign(int id, [FromBody] DeliveryAssignDriverDto dto, CancellationToken ct)
            => FromResponse(await _svc.AssignDriverAsync(id, dto.DriverId, ct));

        /// <summary>Move to the next status (or an explicit allowed one). A driver may advance his own orders.</summary>
        [HttpPost("{id:int}/advance")]
        [Authorize]
        public async Task<IActionResult> Advance(int id, [FromBody] DeliveryAdvanceDto? dto, CancellationToken ct)
            => FromResponse(await _svc.AdvanceAsync(id, dto?.ToStatus, dto?.Note, ct));

        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, [FromBody] DeliveryCancelDto? dto, CancellationToken ct)
            => FromResponse(await _svc.CancelAsync(id, dto?.Reason, ct));
    }
}
