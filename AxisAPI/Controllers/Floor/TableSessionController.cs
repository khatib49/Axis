using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Floor
{
    /// <summary>Table sessions (open / seat / transfer / merge / close) plus courses, bill splits and tips.</summary>
    [ApiController]
    [Route("api/floor/sessions")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class TableSessionController : ControllerBase
    {
        private readonly ITableSessionService _sessions;
        private readonly IFloorBillingService _billing;

        public TableSessionController(ITableSessionService sessions, IFloorBillingService billing)
        {
            _sessions = sessions;
            _billing = billing;
        }

        private IActionResult FromResponse<T>(BaseResponse<T> r) => r.Success ? Ok(r) : BadRequest(r);

        // ── Sessions ─────────────────────────────────────────────────
        [HttpGet("open")]
        public async Task<IActionResult> ListOpen(CancellationToken ct) => Ok(await _sessions.ListOpenAsync(ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var s = await _sessions.GetAsync(id, ct);
            return s is null ? NotFound() : Ok(s);
        }

        [HttpGet("by-transaction/{transactionId:int}")]
        public async Task<IActionResult> GetByTransaction(int transactionId, CancellationToken ct)
        {
            var s = await _sessions.GetByTransactionAsync(transactionId, ct);
            return s is null ? NotFound() : Ok(s);
        }

        [HttpPost("open")]
        public async Task<IActionResult> OpenTable([FromBody] OpenTableRequestDto dto, CancellationToken ct)
            => FromResponse(await _sessions.OpenTableAsync(dto, ct));

        [HttpPost("seat")]
        public async Task<IActionResult> SeatInvoice([FromBody] SeatInvoiceRequestDto dto, CancellationToken ct)
            => FromResponse(await _sessions.SeatInvoiceAsync(dto, ct));

        [HttpPost("{id:int}/transfer")]
        public async Task<IActionResult> Transfer(int id, [FromBody] TransferSessionRequestDto dto, CancellationToken ct)
            => FromResponse(await _sessions.TransferAsync(id, dto, ct));

        [HttpPost("merge")]
        public async Task<IActionResult> Merge([FromBody] MergeInvoicesRequestDto dto, CancellationToken ct)
            => FromResponse(await _sessions.MergeAsync(dto, ct));

        /// <summary>Call after the till closed the invoice (CloseOpenInvoice) to release the table.</summary>
        [HttpPost("close/{transactionId:int}")]
        public async Task<IActionResult> Close(int transactionId, CancellationToken ct)
            => FromResponse(await _sessions.CloseByTransactionAsync(transactionId, ct));

        // ── Courses ──────────────────────────────────────────────────
        [HttpGet("courses/{transactionId:int}")]
        public async Task<IActionResult> ListCourses(int transactionId, CancellationToken ct)
            => Ok(await _billing.ListCoursesAsync(transactionId, ct));

        [HttpPost("courses/{transactionId:int}")]
        public async Task<IActionResult> SetCourses(int transactionId, [FromBody] SetCoursesRequestDto dto, CancellationToken ct)
            => FromResponse(await _billing.SetCoursesAsync(transactionId, dto, ct));

        [HttpPost("courses/{transactionId:int}/fire/{courseNumber:int}")]
        public async Task<IActionResult> FireCourse(int transactionId, int courseNumber, CancellationToken ct)
            => FromResponse(await _billing.FireCourseAsync(transactionId, courseNumber, ct));

        // ── Seat lines / bill by seat ────────────────────────────────
        /// <summary>Bulk seat + course assignment for invoice lines (Append=true adds to the seat, false sets, 0 removes).</summary>
        [HttpPut("{transactionId:int}/lines")]
        public async Task<IActionResult> SetLines(int transactionId, [FromBody] SetLinesRequestDto dto, CancellationToken ct)
            => FromResponse(await _billing.SetLinesAsync(transactionId, dto, ct));

        /// <summary>Invoice lines grouped by seat with per-seat subtotals. bySeat=false returns the same payload (one "Table" group when nothing is assigned).</summary>
        [HttpGet("{transactionId:int}/bill")]
        public async Task<IActionResult> Bill(int transactionId, [FromQuery] bool bySeat = true, CancellationToken ct = default)
        {
            var r = await _billing.GetBillBySeatAsync(transactionId, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        /// <summary>One split per seat with the seat's net amount (replaces unpaid splits).</summary>
        [HttpPost("splits/{transactionId:int}/by-seat")]
        public async Task<IActionResult> SplitBySeat(int transactionId, CancellationToken ct)
            => FromResponse(await _billing.CreateSeatSplitsAsync(transactionId, ct));

        // ── Bill splits ──────────────────────────────────────────────
        [HttpGet("splits/{transactionId:int}")]
        public async Task<IActionResult> ListSplits(int transactionId, CancellationToken ct)
        {
            var r = await _billing.ListSplitsAsync(transactionId, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        [HttpPost("splits/{transactionId:int}/equal")]
        public async Task<IActionResult> SplitEqual(int transactionId, [FromBody] EqualSplitRequestDto dto, CancellationToken ct)
            => FromResponse(await _billing.CreateEqualSplitsAsync(transactionId, dto.Ways, ct));

        [HttpPost("splits/{transactionId:int}/custom")]
        public async Task<IActionResult> SplitCustom(int transactionId, [FromBody] CustomSplitRequestDto dto, CancellationToken ct)
            => FromResponse(await _billing.CreateCustomSplitsAsync(transactionId, dto, ct));

        [HttpPost("splits/pay/{splitId:int}")]
        public async Task<IActionResult> PaySplit(int splitId, [FromBody] PaySplitRequestDto dto, CancellationToken ct)
            => FromResponse(await _billing.PaySplitAsync(splitId, dto, ct));

        [HttpDelete("splits/item/{splitId:int}")]
        public async Task<IActionResult> DeleteSplit(int splitId, CancellationToken ct)
            => FromResponse(await _billing.DeleteSplitAsync(splitId, ct));

        // ── Tips ─────────────────────────────────────────────────────
        [HttpGet("tips/report")]
        public async Task<IActionResult> TipsReport([FromQuery] DateTime? date, CancellationToken ct)
            => Ok(await _billing.DailyTipsReportAsync(date ?? DateTime.UtcNow, ct));

        [HttpGet("tips/{transactionId:int}")]
        public async Task<IActionResult> ListTips(int transactionId, CancellationToken ct)
            => Ok(await _billing.ListTipsAsync(transactionId, ct));

        [HttpPost("tips/{transactionId:int}")]
        public async Task<IActionResult> AddTip(int transactionId, [FromBody] TipCreateDto dto, CancellationToken ct)
            => FromResponse(await _billing.AddTipAsync(transactionId, dto, ct));

        [HttpDelete("tips/item/{tipId:int}")]
        public async Task<IActionResult> DeleteTip(int tipId, CancellationToken ct)
            => FromResponse(await _billing.DeleteTipAsync(tipId, ct));
    }
}
