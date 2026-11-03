using Application.DTOs;

namespace Application.IServices
{
    public interface IOfflineSyncService
    {
        /// <summary>
        /// Executes (or replays) each op in order. Never throws for a single
        /// op's failure; every op gets its own <see cref="OfflineOpResult"/>.
        /// </summary>
        Task<OfflineSyncResponse> SyncAsync(OfflineSyncRequest request, string createdBy, int? branchId, CancellationToken ct = default);
    }

    public interface IOfflineSnapshotService
    {
        Task<OfflineSnapshotDto> BuildAsync(CancellationToken ct = default);
    }
}
