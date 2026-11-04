using Application.DTOs;
using Application.IServices;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Phase4
{
    /// <summary>3D models for menu items. Public list for the menu / shop / self-order / kiosk; uploads are admin-only.</summary>
    [ApiController]
    [Route("api/menu-3d")]
    [Authorize(Roles = "admin")]
    public class Menu3DController : ControllerBase
    {
        private const long MaxUploadBytes = 80L * 1024 * 1024;
        private readonly IItemModel3DService _svc;
        public Menu3DController(IItemModel3DService svc) { _svc = svc; }

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));

        [HttpGet("public")]
        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 60, WindowSeconds = 60)]
        public async Task<IActionResult> Public(CancellationToken ct) => Ok(await _svc.ListPublicAsync(ct));

        /// <summary>multipart/form-data: glb (required the first time), usdz, poster.</summary>
        [HttpPost("{itemId:int}")]
        [RequestSizeLimit(MaxUploadBytes)]
        [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
        public async Task<IActionResult> Upload(int itemId, IFormFile? glb, IFormFile? usdz, IFormFile? poster, CancellationToken ct)
        {
            var r = await _svc.UploadAsync(itemId, glb, usdz, poster, ct);
            return r.Success ? Ok(r) : r.Error == "NotFound" ? NotFound(r) : BadRequest(r);
        }

        [HttpPut("{itemId:int}/active")]
        public async Task<IActionResult> SetActive(int itemId, [FromQuery] bool value, CancellationToken ct)
        {
            var r = await _svc.SetActiveAsync(itemId, value, ct);
            return r.Success ? Ok(r) : NotFound(r);
        }

        [HttpDelete("{itemId:int}")]
        public async Task<IActionResult> Delete(int itemId, CancellationToken ct)
            => await _svc.DeleteAsync(itemId, ct) ? NoContent() : NotFound();
    }
}
