using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A one-shot message to a segment over one channel.
    /// Status: Draft | Scheduled | Sending | Sent | Cancelled | Failed.
    /// Body placeholders: {{name}} {{firstName}} {{lastVisit}} {{code}} {{unsubscribeUrl}}.
    /// </summary>
    [Table("Mkt_Campaigns")]
    public class MktCampaign
    {
        public int Id { get; set; }
        [Required][MaxLength(150)] public string Name { get; set; } = default!;
        /// <summary>Email | Sms | WhatsApp</summary>
        [Required][MaxLength(20)] public string Channel { get; set; } = "Email";
        public int SegmentId { get; set; }
        public MktSegment? Segment { get; set; }
        [MaxLength(300)] public string? Subject { get; set; }
        public string Body { get; set; } = "";
        /// <summary>Meta template name used when Channel = WhatsApp (params: [firstName]).</summary>
        [MaxLength(200)] public string? WhatsAppTemplate { get; set; }
        /// <summary>Optional promo / coupon code rendered by {{code}}.</summary>
        [MaxLength(60)] public string? PromoCode { get; set; }
        [Required][MaxLength(20)] public string Status { get; set; } = "Draft";
        public DateTime? ScheduledFor { get; set; }
        public DateTime? SentOn { get; set; }
        public int AudienceCount { get; set; }
        public int SentCount { get; set; }
        public int FailedCount { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [MaxLength(200)] public string CreatedBy { get; set; } = "system";
        /// <summary>Optional branch scope (no FK; multi-branch scoping later).</summary>
        public int? BranchId { get; set; }
    }
}
