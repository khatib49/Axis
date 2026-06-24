using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    /// <summary>
    /// Read-only feed of admin Create/Update/Delete actions across the
    /// allow-listed entities (Items, Categories, Discounts, Channels,
    /// Suppliers, Purchases, Expenses, Accounts, Settings, etc.).
    /// Writes are produced automatically by AdminAuditInterceptor.
    /// </summary>
    [ApiController]
    [Route("api/admin-audit")]
    [Authorize(Roles = "admin")]
    public class AdminAuditController : ControllerBase
    {
        private readonly IAdminAuditService _svc;
        public AdminAuditController(IAdminAuditService svc) => _svc = svc;

        // GET /api/admin-audit?entityType=Item&action=Updated&changedBy=&from=&to=&page=1&pageSize=50
        [HttpGet]
        public async Task<IActionResult> List(
            [FromQuery] string? entityType,
            [FromQuery] string? action,
            [FromQuery] string? changedBy,
            [FromQuery] int? entityId,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken ct = default)
        {
            var filter = new AdminAuditFilterDto(
                entityType, action, changedBy, entityId, from, to, page, pageSize);
            var result = await _svc.ListAsync(filter, ct);
            return Ok(result);
        }

        // GET /api/admin-audit/entity-types  → distinct EntityType values for the FE dropdown
        [HttpGet("entity-types")]
        public async Task<IActionResult> EntityTypes(CancellationToken ct)
            => Ok(await _svc.DistinctEntityTypesAsync(ct));

        // No POST/PUT/DELETE — log is append-only and produced by the interceptor.
    }
}
