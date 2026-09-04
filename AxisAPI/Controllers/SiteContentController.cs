using System.Text.Json;
using Application.DTOs;
using Application.IServices;
using Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers
{
    /// <summary>
    /// Public read of the website content document (copy, prices, hours,
    /// image paths). Anonymous — it's exactly what the website shows.
    /// </summary>
    [ApiController]
    [Route("api/site-content")]
    [AllowAnonymous]
    public class SiteContentController : ControllerBase
    {
        public const string WebsiteKey = "website";
        private readonly ISiteContentService _svc;

        public SiteContentController(ISiteContentService svc) => _svc = svc;

        /// <summary>
        /// Stored document, or an empty object when nothing was saved yet.
        /// Served from memory with an ETag: browsers that already hold the
        /// current version get a 304 and no body.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            var (json, etag) = await _svc.GetWithEtagAsync(WebsiteKey, ct);

            Response.Headers.ETag = etag;
            Response.Headers.CacheControl = "no-cache";

            var ifNoneMatch = Request.Headers.IfNoneMatch.ToString();
            if (!string.IsNullOrEmpty(ifNoneMatch) &&
                ifNoneMatch.Split(',').Select(v => v.Trim()).Any(v => v == etag || v == "W/" + etag))
                return StatusCode(StatusCodes.Status304NotModified);

            return Content(json, "application/json");
        }
    }

    /// <summary>
    /// Admin side: replace the document and upload images used by it.
    /// Separate class because the public controller is [AllowAnonymous]
    /// at class level, which would override a method-level [Authorize].
    /// </summary>
    [ApiController]
    [Route("api/admin/site-content")]
    [Authorize(Roles = "admin")]
    public class SiteContentAdminController : ControllerBase
    {
        private const int MaxJsonBytes = 1024 * 1024;

        private readonly ISiteContentService _svc;
        private readonly IMediaStorageService _media;
        private readonly IHttpContextAccessor _http;
        private readonly ILogger<SiteContentAdminController> _logger;

        public SiteContentAdminController(
            ISiteContentService svc,
            IMediaStorageService media,
            IHttpContextAccessor http,
            ILogger<SiteContentAdminController> logger)
        {
            _svc = svc;
            _media = media;
            _http = http;
            _logger = logger;
        }

        private string Actor => _http.HttpContext?.User?.Identity?.Name ?? "admin";

        /// <summary>Replace the whole document. Body must be a JSON object.</summary>
        [HttpPut]
        public async Task<IActionResult> Save([FromBody] JsonElement body, CancellationToken ct)
        {
            if (body.ValueKind != JsonValueKind.Object)
                return BadRequest(new { message = "Content must be a JSON object." });

            string json;
            try { json = body.GetRawText(); }
            catch (InvalidOperationException)
            {
                // Body bytes weren't valid UTF-8 — refuse rather than store garbage.
                return BadRequest(new { message = "Content must be valid UTF-8 JSON." });
            }
            if (json.Length > MaxJsonBytes)
                return BadRequest(new { message = "Content is too large (max 1 MB). Upload images instead of embedding them." });

            await _svc.SaveJsonAsync(SiteContentController.WebsiteKey, json, Actor, ct);
            _logger.LogInformation("Website content saved by {Actor} ({Size} KB)", Actor, json.Length / 1024);
            return NoContent();
        }

        /// <summary>Upload an image for the website (hero, gallery, service card…).</summary>
        [HttpPost("images")]
        [RequestSizeLimit(20L * 1024 * 1024)]
        public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken ct)
        {
            try
            {
                var path = await _media.SaveAsync(file, "site", MediaKind.Image, ct);
                return Ok(new MediaUploadResultDto(path, "/" + path));
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Website image upload failed");
                return StatusCode(500, new { message = "Upload failed." });
            }
        }
    }
}
