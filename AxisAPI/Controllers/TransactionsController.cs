using Application.DTOs;
using Application.IServices;
using AxisAPI.Logging;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    [ApiController]
    [Route("api/transactions")]
    public class TransactionsController : ControllerBase
    {
        private readonly ITransactionRecordService _transactionService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TransactionsController(ITransactionRecordService transationService, IHttpContextAccessor httpContextAccessor)
        {
            _transactionService = transationService;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Main-dashboard transactions list. Filters by created date range
        /// + optional channelId. Used by the new "Transactions" card on the
        /// home dashboard.
        /// </summary>
        [HttpGet("dashboard")]
        [Authorize]
        public async Task<IActionResult> DashboardList(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? channelId,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            var filter = new DashboardTransactionsFilterDto(from, to, channelId, page, pageSize);
            var result = await _transactionService.GetDashboardTransactionsAsync(filter, ct);
            return Ok(result);
        }

        /// <summary>
        /// Streams the same filtered transactions as a .xlsx file. Shares
        /// the same query path as DashboardList — pass Page=null /
        /// PageSize=null on the service to get every matching row.
        /// </summary>
        [HttpGet("dashboard/export")]
        [Authorize]
        public async Task<IActionResult> DashboardExport(
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? channelId,
            CancellationToken ct = default)
        {
            // Page=null + PageSize=null → service skips paging entirely.
            var filter = new DashboardTransactionsFilterDto(from, to, channelId, null, null);
            var result = await _transactionService.GetDashboardTransactionsAsync(filter, ct);
            var rows = result.Data ?? new List<DashboardTransactionRowDto>();

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Transactions");

            // Header
            string[] headers = {
                "ID", "Created (UTC)", "Created By", "Status", "Channel",
                "Total", "Items", "Comment"
            };
            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(1, i + 1).Value = headers[i];
                ws.Cell(1, i + 1).Style.Font.Bold = true;
                ws.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E79");
                ws.Cell(1, i + 1).Style.Font.FontColor = XLColor.White;
            }

            int row = 2;
            foreach (var t in rows)
            {
                ws.Cell(row, 1).Value = t.Id;
                ws.Cell(row, 2).Value = t.CreatedOn;
                ws.Cell(row, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
                ws.Cell(row, 3).Value = t.CreatedBy;
                ws.Cell(row, 4).Value = t.StatusId;
                ws.Cell(row, 5).Value = t.ChannelName ?? "Direct";
                ws.Cell(row, 6).Value = t.TotalPrice;
                ws.Cell(row, 6).Style.NumberFormat.Format = "$#,##0.00";
                ws.Cell(row, 7).Value = t.ItemsCount;
                ws.Cell(row, 8).Value = t.Comment ?? string.Empty;
                row++;
            }

            // Totals row
            if (rows.Count > 0)
            {
                ws.Cell(row, 1).Value = "TOTAL";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 6).FormulaA1 = $"=SUM(F2:F{row - 1})";
                ws.Cell(row, 6).Style.Font.Bold = true;
                ws.Cell(row, 6).Style.NumberFormat.Format = "$#,##0.00";
                ws.Cell(row, 7).FormulaA1 = $"=SUM(G2:G{row - 1})";
                ws.Cell(row, 7).Style.Font.Bold = true;
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            ms.Position = 0;

            var fromStr = from?.ToString("yyyyMMdd") ?? "all";
            var toStr = to?.ToString("yyyyMMdd") ?? "all";
            var filename = $"transactions_{fromStr}_{toStr}.xlsx";

            return File(ms.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                filename);
        }

        [HttpDelete("{transactionId:int}/items/{itemId:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> RemoveItemFromOpenInvoice(int transactionId, int itemId, CancellationToken ct)
        {
            var result = await _transactionService.RemoveItemFromOpenInvoiceAsync(transactionId, itemId, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var item = await _transactionService.GetAsync(id, ct);
            if (item is null) return NotFound();
            return Ok(item);
        }

        [HttpGet("{id:int}/details")]
        public async Task<IActionResult> GetWithItems(int id, CancellationToken ct)
        {
            var item = await _transactionService.GetWithItemsAsync(id, ct);
            if (item is null) return NotFound();
            return Ok(item);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> List([FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var categories = await _transactionService.ListAsync(pagination, ct);
            return Ok(categories);
        }

        [HttpPut("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Update(int id, TransactionUpdateDto dto, CancellationToken ct)
        {
            var success = await _transactionService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }
        [Authorize]
        [HttpPost("CreateGame")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Create(TransactionCreateDto dto, CancellationToken ct)
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var created = await _transactionService.CreateAsync(dto, createdBy, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }


        
        //[HttpPost("CreateGameSession")]
        //[Authorize(Roles = "admin,gamecashier")]
        //[LogOnError]
        //public async Task<IActionResult> CreateGameSession(int gameId, int gameSettingId, int hours, int status,int setId, CancellationToken ct)
        //{
        //    var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
        //    var created = await _transactionService.CreateGameSession(gameId, gameSettingId, hours, status, createdBy, setId,ct);
        //    return CreatedAtAction(nameof(Get), new { id = created.Data.Id }, created);
        //}

        [HttpPost("CreateGameSession")]
        [Authorize(Roles = "admin,gamecashier")]
        public async Task<IActionResult> CreateGameSession(int gameId, int gameSettingId, int hours, 
            int status, int setId, int discountId, int? userId, CancellationToken ct,
            int numberOfPersons = 1, bool isDayPass = false, string comment = "")
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var created = await _transactionService.CreateGameSession(userId,
                gameId, gameSettingId, hours, status, createdBy ?? "", setId, discountId ,ct, numberOfPersons, isDayPass,comment);

            return created.Success ? Ok(created) : BadRequest(created);
        }

        [Route("UpdateOpenInvoiceSet/{invoiceId}")]
        //[Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPut]
        [LogOnError]
        public async Task<IActionResult> UpdateOpenInvoiceSet(int invoiceId, [FromBody] UpdateSetRequest request, CancellationToken ct)
        {
            var updatedBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var result = await _transactionService.UpdateOpenInvoiceSet(
                invoiceId, request.SetId, updatedBy, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Route("CreateCoffeeShopOrder")]
       // [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPost]
        [LogOnError]
        public async Task<IActionResult> CreateCoffeeShopOrder([FromBody] CreateCoffeeShopOrderRequest request, CancellationToken ct )
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var created = await _transactionService.CreateCoffeeShopOrder(request.UserId, request.DiscountId, request.ItemsRequest, createdBy, ct, request.Comment, request.IsOpenInvoice, request.setId, request.ChannelId);
            return created.Success ? Ok(created) : BadRequest(created);
        }

        [Route("GetOpenBoardGameSessions")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpGet]
        [LogOnError]
        public async Task<IActionResult> GetOpenBoardGameSessions(CancellationToken ct)
        {
            var result = await _transactionService.GetOpenBoardGameSessions(ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }


        [Route("GetOpenPs5Sessions")]
        [Authorize(Roles = "admin,cashier,gamecashier")]
        [HttpGet]
        [LogOnError]
        public async Task<IActionResult> GetOpenPs5Sessions(CancellationToken ct)
        {
            var result = await _transactionService.GetOpenPs5Sessions(ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }
        [HttpPost("sessions/{invoiceId:int}/close")]
        public async Task<ActionResult<BaseResponse<TransactionDto>>> CloseSession(int invoiceId,CancellationToken ct)
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var result = await _transactionService.CloseGameSession(invoiceId, createdBy, ct);
            if (!result.Success)
                return BadRequest(result);

            return Ok(result);
        }

        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var success = await _transactionService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }

        [Route("GetOpenFnbInvoices")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpGet]
        [LogOnError]
        public async Task<IActionResult> GetOpenFnbInvoices(CancellationToken ct)
        {
            var result = await _transactionService.GetOpenFnbInvoices(ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

        [Route("AddItemsToOpenInvoice/{invoiceId}")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPost]
        [LogOnError]
        public async Task<IActionResult> AddItemsToOpenInvoice(
            int invoiceId,
            List<OrderItemRequest> itemsRequest,
            CancellationToken ct)
        {
            var updatedBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var result = await _transactionService.AddItemsToOpenInvoice(
                invoiceId, itemsRequest, updatedBy, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }


        [Route("CloseOpenInvoice/{invoiceId}")]
        [Authorize(Roles = "admin,cashier,gamecashier")]
        [HttpPost]
        [LogOnError]
        public async Task<IActionResult> CloseOpenInvoice(int invoiceId, CancellationToken ct)
        {
            var updatedBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var result = await _transactionService.CloseOpenInvoice(invoiceId, updatedBy, ct);
            return result.Success ? Ok(result) : BadRequest(result);
        }

    }
}
