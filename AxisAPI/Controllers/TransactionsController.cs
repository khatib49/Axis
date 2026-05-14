using Application.DTOs;
using Application.IServices;
using AxisAPI.Logging;
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
            var created = await _transactionService.CreateCoffeeShopOrder(request.UserId, request.DiscountId, request.ItemsRequest, createdBy, ct, request.Comment, request.IsOpenInvoice, request.setId);
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
