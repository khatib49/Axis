using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Bi
{
    [ApiController]
    [Route("api/bi/owner-summary")]
    public class BiOwnerSummaryController : ControllerBase
    {
        private readonly IOwnerSummaryService _svc;
        public BiOwnerSummaryController(IOwnerSummaryService svc) => _svc = svc;

        /// <summary>Compact mobile owner view. ?day=yyyy-MM-dd (default today), ?branchId= (default all branches).</summary>
        [HttpGet]
        [Authorize(Roles = "admin,admin_fnb")]
        public async Task<IActionResult> Get([FromQuery] DateTime? day, [FromQuery] int? branchId, CancellationToken ct) =>
            Ok(await _svc.BuildAsync(day, branchId, ct));
    }
}
