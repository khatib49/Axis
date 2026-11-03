using Application.Common;
using Application.DTOs;
using Application.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AxisAPI.Controllers.Offline
{
    /// <summary>
    /// Till offline resilience: replay of locally queued orders (idempotent)
    /// and the compact snapshot the PWA caches for an internet cut.
    /// </summary>
    [ApiController]
    [Route("api/offline")]
    [Authorize(Roles = "admin,cashier,gamecashier,admin_fnb")]
    public class OfflineController : ControllerBase
    {
        private readonly IOfflineSyncService _sync;
        private readonly IOfflineSnapshotService _snapshot;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<OfflineController> _log;

        public OfflineController(IOfflineSyncService sync, IOfflineSnapshotService snapshot, ICurrentUserAccessor user, ILogger<OfflineController> log)
        {
            _sync = sync;
            _snapshot = snapshot;
            _user = user;
            _log = log;
        }

        /// <summary>
        /// Replays queued ops in order. Always 200 with a per-op result list;
        /// a single failing op never fails the batch.
        /// </summary>
        [HttpPost("sync")]
        public async Task<ActionResult<OfflineSyncResponse>> Sync([FromBody] OfflineSyncRequest request, CancellationToken ct)
        {
            if (request is null) return BadRequest("Body is required.");
            if (request.Ops is null) return BadRequest("ops is required.");
            if (request.Ops.Count > 100) return BadRequest("At most 100 ops per sync call.");

            var result = await _sync.SyncAsync(request, _user.UserName, _user.BranchId, ct);
            return Ok(result);
        }

        /// <summary>Compact menu/discount/channel bundle for offline use.</summary>
        [HttpGet("snapshot")]
        [ResponseCache(NoStore = true)]
        public async Task<ActionResult<OfflineSnapshotDto>> Snapshot(CancellationToken ct)
        {
            try
            {
                var snap = await _snapshot.BuildAsync(ct);
                return Ok(snap);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "Offline snapshot failed");
                return StatusCode(500, new { message = "Snapshot could not be built." });
            }
        }
    }
}
