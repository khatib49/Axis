using System.Security.Claims;
using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    /// <summary>
    /// Admin → Roles &amp; Permissions: create roles and choose which dashboard
    /// pages (and therefore which API calls) each role may use.
    /// </summary>
    [ApiController]
    [Route("api/admin/roles")]
    [Authorize(Roles = "admin")]
    public class RolesController : ControllerBase
    {
        private readonly IRolePageService _svc;
        private readonly ILogger<RolesController> _logger;

        public RolesController(IRolePageService svc, ILogger<RolesController> logger)
        {
            _svc = svc;
            _logger = logger;
        }

        /// <summary>Every page that can be granted.</summary>
        [HttpGet("catalog")]
        public IActionResult Catalog() => Ok(_svc.Catalog());

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] RoleCreateRequest body, CancellationToken ct)
        {
            try
            {
                var role = await _svc.CreateAsync(body.Name, body.Pages, ct);
                _logger.LogInformation("Role {Role} created by {Actor} with {Count} pages", role.Name, User.Identity?.Name, role.Pages.Count);
                return Ok(role);
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpPut("{name}/pages")]
        public async Task<IActionResult> SetPages(string name, [FromBody] RolePagesRequest body, CancellationToken ct)
        {
            try
            {
                var role = await _svc.SetPagesAsync(name, body.Pages ?? Array.Empty<string>(), ct);
                _logger.LogInformation("Role {Role} pages set by {Actor}: {Pages}", role.Name, User.Identity?.Name, string.Join(",", role.Pages));
                return Ok(role);
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
        }

        [HttpDelete("{name}")]
        public async Task<IActionResult> Delete(string name, CancellationToken ct)
        {
            try
            {
                await _svc.DeleteAsync(name, ct);
                _logger.LogInformation("Role {Role} deleted by {Actor}", name, User.Identity?.Name);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }
    }

    /// <summary>What the signed-in user may open — drives the sidebar and route guards.</summary>
    [ApiController]
    [Route("api/roles")]
    [Authorize]
    public class MyPagesController : ControllerBase
    {
        private readonly IRolePageService _svc;

        public MyPagesController(IRolePageService svc) => _svc = svc;

        [HttpGet("my-pages")]
        public async Task<IActionResult> MyPages(CancellationToken ct)
        {
            var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value);
            var pages = await _svc.GetPagesForRolesAsync(roles, ct);
            return Ok(pages.OrderBy(p => p).ToList());
        }
    }
}
