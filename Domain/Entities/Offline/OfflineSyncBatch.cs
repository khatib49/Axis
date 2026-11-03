using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Audit row for every POST api/offline/sync call.</summary>
    [Table("Offline_SyncBatches")]
    public class OfflineSyncBatch
    {
        public int Id { get; set; }

        [MaxLength(128)]
        public string ClientId { get; set; } = default!;

        public DateTime ReceivedOn { get; set; } = DateTime.UtcNow;
        public int OpsCount { get; set; }
        public int SucceededCount { get; set; }
        public int FailedCount { get; set; }

        [MaxLength(256)]
        public string ReceivedBy { get; set; } = default!;

        public int? BranchId { get; set; }
    }
}
