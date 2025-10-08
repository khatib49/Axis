using Application.DTOs;
using Application.IServices;
using Domain.Entities;
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
        public async Task<IActionResult> List([FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var categories = await _transactionService.ListAsync(pagination, ct);
            return Ok(categories);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, TransactionUpdateDto dto, CancellationToken ct)
        {
            var success = await _transactionService.UpdateAsync(id, dto, ct);
            if (!success) return NotFound();
            return NoContent();
        }
        [Authorize]
        [HttpPost("CreateGame")]
        public async Task<IActionResult> Create(TransactionCreateDto dto, CancellationToken ct)
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var created = await _transactionService.CreateAsync(dto, createdBy, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }


        
        [HttpPost("CreateGameSession")]
        public async Task<IActionResult> CreateGameSession(int gameId, int gameSettingId, int hours, int status, CancellationToken ct)
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var created = await _transactionService.CreateGameSession(gameId, gameSettingId, hours, status, createdBy, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [Route("CreateCoffeeShopOrder")]
        [HttpPost]
        public async Task<IActionResult> CreateCoffeeShopOrder(List<OrderItemRequest> itemsRequest, CancellationToken ct)
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var created = await _transactionService.CreateCoffeeShopOrder(itemsRequest, createdBy, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }


        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            var success = await _transactionService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
