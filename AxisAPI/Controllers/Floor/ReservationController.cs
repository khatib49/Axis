using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Floor
{
    /// <summary>Reservations for tables, gaming rooms and consoles.</summary>
    [ApiController]
    [Route("api/floor/reservations")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class ReservationController : ControllerBase
    {
        private readonly IReservationService _svc;
        public ReservationController(IReservationService svc) => _svc = svc;

        private IActionResult FromResponse<T>(BaseResponse<T> r) => r.Success ? Ok(r) : BadRequest(r);

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] ReservationListFilterDto filter, CancellationToken ct)
            => Ok(await _svc.ListAsync(filter, ct));

        [HttpGet("today")]
        public async Task<IActionResult> Today([FromQuery] DateTime? date, CancellationToken ct)
            => Ok(await _svc.TodayBoardAsync(date, ct));

        [HttpGet("availability")]
        public async Task<IActionResult> Availability([FromQuery] AvailabilityQueryDto q, CancellationToken ct)
            => Ok(await _svc.CheckAvailabilityAsync(q, ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var r = await _svc.GetAsync(id, ct);
            return r is null ? NotFound() : Ok(r);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] ReservationUpsertDto dto, CancellationToken ct)
            => FromResponse(await _svc.CreateAsync(dto, ct));

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] ReservationUpsertDto dto, CancellationToken ct)
            => FromResponse(await _svc.UpdateAsync(id, dto, ct));

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
            => FromResponse(await _svc.DeleteAsync(id, ct));

        [HttpPost("{id:int}/confirm")]
        public async Task<IActionResult> Confirm(int id, CancellationToken ct) => FromResponse(await _svc.ConfirmAsync(id, ct));

        [HttpPost("{id:int}/seat")]
        public async Task<IActionResult> Seat(int id, [FromBody] SeatReservationRequestDto? dto, CancellationToken ct)
            => FromResponse(await _svc.SeatAsync(id, dto ?? new SeatReservationRequestDto(), ct));

        [HttpPost("{id:int}/no-show")]
        public async Task<IActionResult> NoShow(int id, CancellationToken ct) => FromResponse(await _svc.NoShowAsync(id, ct));

        [HttpPost("{id:int}/cancel")]
        public async Task<IActionResult> Cancel(int id, CancellationToken ct) => FromResponse(await _svc.CancelAsync(id, ct));

        [HttpPost("{id:int}/complete")]
        public async Task<IActionResult> Complete(int id, CancellationToken ct) => FromResponse(await _svc.CompleteAsync(id, ct));

        /// <summary>Public online request form. Creates a Pending reservation with Source=Online.</summary>
        [HttpPost("public")]
        [AllowAnonymous]
        public async Task<IActionResult> PublicRequest([FromBody] PublicReservationRequestDto dto, CancellationToken ct)
            => FromResponse(await _svc.PublicRequestAsync(dto, ct));

        /// <summary>Public list of active tables so the online form can offer a choice (code/name/capacity only).</summary>
        [HttpGet("public/tables")]
        [AllowAnonymous]
        public async Task<IActionResult> PublicTables([FromServices] IFloorService floor, CancellationToken ct)
        {
            var tables = await floor.ListTablesAsync(null, false, ct);
            return Ok(tables.Select(t => new { t.Id, t.Code, t.Name, t.Capacity, t.AreaName }));
        }
    }
}
