using Application.DTOs;
using Application.IServices;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    /// <summary>
    /// Admin CMS for public event pages — create/edit an event, upload its
    /// promo video and hero image, toggle which payment methods are live,
    /// and publish it.
    /// </summary>
    [ApiController]
    [Route("api/admin/events")]
    [Authorize(Roles = "admin")]
    public class EventsAdminController : ControllerBase
    {
        private readonly IEventService _svc;
        private readonly IMediaStorageService _media;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<EventsAdminController> _logger;

        public EventsAdminController(
            IEventService svc,
            IMediaStorageService media,
            IHttpContextAccessor http,
            ILogger<EventsAdminController> logger)
        {
            _svc = svc;
            _media = media;
            _http = http;
            _logger = logger;
        }

        private string Actor => _http.HttpContext?.User?.Identity?.Name ?? "admin";

        [HttpGet]
        public async Task<IActionResult> List(CancellationToken ct)
            => Ok(await _svc.ListAsync(ct));

        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id, CancellationToken ct)
        {
            var e = await _svc.GetAsync(id, ct);
            return e is null ? NotFound() : Ok(e);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] EventUpsertDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.CreateAsync(dto, Actor, ct)); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] EventUpsertDto dto, CancellationToken ct)
        {
            try { return Ok(await _svc.UpdateAsync(id, dto, ct)); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken ct)
        {
            try { return await _svc.DeleteAsync(id, ct) ? NoContent() : NotFound(); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        /// <summary>
        /// Upload the promo video for an event. Multipart form field name
        /// must be "file". Replaces (and deletes) any previous upload.
        /// </summary>
        [HttpPost("{id:int}/video")]
        [RequestSizeLimit(220L * 1024 * 1024)]
        public async Task<IActionResult> UploadVideo(int id, IFormFile file, CancellationToken ct)
        {
            try
            {
                var path = await _media.SaveAsync(file, "events", MediaKind.Video, ct);
                var ok = await _svc.SetMediaAsync(id, videoPath: path, heroImagePath: null, ct);
                if (!ok) return NotFound();
                return Ok(new MediaUploadResultDto(path, "/" + path));
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Video upload failed for event {Id}", id);
                return StatusCode(500, new { message = "Upload failed." });
            }
        }

        [HttpPost("{id:int}/hero")]
        [RequestSizeLimit(20L * 1024 * 1024)]
        public async Task<IActionResult> UploadHero(int id, IFormFile file, CancellationToken ct)
        {
            try
            {
                var path = await _media.SaveAsync(file, "events", MediaKind.Image, ct);
                var ok = await _svc.SetMediaAsync(id, videoPath: null, heroImagePath: path, ct);
                if (!ok) return NotFound();
                return Ok(new MediaUploadResultDto(path, "/" + path));
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hero upload failed for event {Id}", id);
                return StatusCode(500, new { message = "Upload failed." });
            }
        }

        /// <summary>Clears the uploaded video (falls back to YouTube id, if set).</summary>
        [HttpDelete("{id:int}/video")]
        public async Task<IActionResult> RemoveVideo(int id, CancellationToken ct)
            => await _svc.SetMediaAsync(id, videoPath: "", heroImagePath: null, ct) ? NoContent() : NotFound();
    }
}
