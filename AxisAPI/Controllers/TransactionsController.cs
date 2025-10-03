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

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id, CancellationToken ct)
        {
            var item = await _transactionService.GetAsync(id, ct);
            if (item is null) return NotFound();
            return Ok(item);
        }

        [HttpGet]
        public async Task<IActionResult> List([FromQuery] BasePaginationRequestDto pagination, CancellationToken ct)
        {
            var categories = await _transactionService.ListAsync(pagination, ct);
            return Ok(categories);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, TransactionUpdateDto dto, CancellationToken ct)
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


        [Authorize("cashier")]
        [HttpPost("CreateGameSession")]
        public async Task<IActionResult> CreateGameSession(Guid gameId, Guid gameSettingId, int hours, Guid status, CancellationToken ct)
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var created = await _transactionService.CreateGameSession(gameId, gameSettingId, hours, status, createdBy, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }

        [Authorize("cashierCoffeeShop")]
        [HttpPost]
        public async Task<IActionResult> CreateCoffeeShopOrder(string itemIds, CancellationToken ct)
        {
            var createdBy = _httpContextAccessor.HttpContext?.User?.Identity?.Name;
            var created = await _transactionService.CreateGameSession(gameId, gameSettingId, hours, status, createdBy, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }


        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var success = await _transactionService.DeleteAsync(id, ct);
            if (!success) return NotFound();
            return NoContent();
        }
    }
}
