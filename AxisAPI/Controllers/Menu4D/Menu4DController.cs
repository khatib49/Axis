using Application.DTOs;
using Application.IServices;
using Application.Services.Menu4D;
using AxisAPI.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Menu4D
{
    /// <summary>4D menu studio (admin) and the public parts list used by the menu, shop, kiosk and table ordering.</summary>
    [ApiController]
    [Route("api/menu-4d")]
    [Authorize(Roles = "admin")]
    public class Menu4DController : ControllerBase
    {
        private readonly IMenu4DService _svc;
        public Menu4DController(IMenu4DService svc) { _svc = svc; }

        private IActionResult From<T>(BaseResponse<T> r) => r.Success ? Ok(r)
            : r.Error switch { "NotFound" => NotFound(r), _ => BadRequest(r) };

        [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await _svc.ListAsync(ct));
        [HttpGet("status")] public async Task<IActionResult> Status(CancellationToken ct) => Ok(await _svc.StatusAsync(ct));

        [HttpGet("public")]
        [AllowAnonymous]
        [AnonymousRateLimit(MaxRequests = 60, WindowSeconds = 60)]
        public async Task<IActionResult> Public(CancellationToken ct) => Ok(await _svc.ListPublicAsync(ct));

        [HttpPost("{itemId:int}/analyze")] public async Task<IActionResult> Analyze(int itemId, CancellationToken ct) => From(await _svc.AnalyzeAsync(itemId, ct));

        /// <summary>Labels every unlabelled item in the background (one AI call per photo).</summary>
        [HttpPost("analyze-all")]
        public async Task<IActionResult> AnalyzeAll(CancellationToken ct)
        {
            var n = await _svc.CountUnlabelledAsync(ct);
            if (n > 0) Hangfire.BackgroundJob.Enqueue<Menu4DJobs>(j => j.LabelAll());
            return Ok(new Menu4DBulkResultDto(n, n == 0 ? "Every item with a photo already has its parts." : $"Labelling {n} items in the background. Refresh in a few minutes."));
        }

        [HttpPut("{itemId:int}")] public async Task<IActionResult> Update(int itemId, [FromBody] Item4DUpdateDto dto, CancellationToken ct) => From(await _svc.UpdateAsync(itemId, dto, ct));
        [HttpPost("publish-all")] public async Task<IActionResult> PublishAll(CancellationToken ct) => Ok(await _svc.PublishAllAsync(ct));
        [HttpPost("{itemId:int}/model")] public async Task<IActionResult> MakeModel(int itemId, CancellationToken ct) => From(await _svc.QueueModelAsync(itemId, ct));
        [HttpPost("models-all")] public async Task<IActionResult> MakeAllModels(CancellationToken ct) => Ok(await _svc.QueueAllModelsAsync(ct));

        /// <summary>Runs the queue now instead of waiting for the next minute.</summary>
        [HttpPost("process")] public async Task<IActionResult> Process(CancellationToken ct) => Ok(new { changed = await _svc.ProcessModelQueueAsync(ct) });
    }
}
