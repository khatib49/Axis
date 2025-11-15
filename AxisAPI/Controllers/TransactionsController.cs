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
        public async Task<IActionResult> CreateGameSession(
     int gameId, int gameSettingId, int hours, int status, int setId,
    string? phoneNumber,
    CancellationToken ct)
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var created = await _transactionService.CreateGameSession( phoneNumber,
                gameId, gameSettingId, hours, status, createdBy ?? "", setId, ct);

            return created.Success ? Ok(created) : BadRequest(created);
        }

        [Route("CreateCoffeeShopOrder")]
        [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
        [HttpPost]
        [LogOnError]
        public async Task<IActionResult> CreateCoffeeShopOrder(string? phoneNumber, List<OrderItemRequest> itemsRequest, CancellationToken ct)
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var created = await _transactionService.CreateCoffeeShopOrder(phoneNumber, itemsRequest, createdBy, ct);
            return created.Success ? Ok(created) : BadRequest(created);
        }


        [HttpDelete("{id:int}")]
        [Authorize(Roles = "admin")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var success = await _transactionService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
