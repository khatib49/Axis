using System.ComponentModel.DataAnnotations;

namespace Domain.Entities
{
    /// <summary>
    /// The 4D menu data for one item: the labelled parts guests can tap
    /// (found by AI from the photo, corrected by staff) and the state of the
    /// AI photo-to-3D job that makes a model for "see it on your table".
    /// </summary>
    public class Item4DProfile
    {
        [Key] public int Id { get; set; }
        public int ItemId { get; set; }

        /// <summary>JSON array of parts: name, detail, x/y on the photo (percent), optional 3D position and normal.</summary>
        public string? PartsJson { get; set; }
        /// <summary>Guests only see the parts once staff publish them.</summary>
        public bool PartsPublished { get; set; }
        /// <summary>Real width of the dish in centimetres, so it appears life-size on the table.</summary>
        public decimal? RealSizeCm { get; set; }

        /// <summary>None | Queued | Running | Done | Failed</summary>
        [Required][MaxLength(20)] public string GenStatus { get; set; } = Item4DGenStatus.None;
        [MaxLength(40)] public string? GenProvider { get; set; }
        [MaxLength(120)] public string? GenTaskId { get; set; }
        public int GenProgress { get; set; }
        [MaxLength(1000)] public string? GenError { get; set; }
        public DateTime? GenQueuedOn { get; set; }
        public DateTime? GenStartedOn { get; set; }
        public DateTime? GenFinishedOn { get; set; }

        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
        [Required][MaxLength(256)] public string UpdatedBy { get; set; } = "system";
    }

    public static class Item4DGenStatus
    {
        public const string None = "None";
        public const string Queued = "Queued";
        public const string Running = "Running";
        public const string Done = "Done";
        public const string Failed = "Failed";
    }
}
