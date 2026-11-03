using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// One delivery attempt per audience member (or per test-send address, UserId null).
    /// Status: Pending | Sent | Failed | Skipped.
    /// </summary>
    [Table("Mkt_CampaignRecipients")]
    public class MktCampaignRecipient
    {
        public int Id { get; set; }
        public int CampaignId { get; set; }
        public MktCampaign? Campaign { get; set; }
        public int? UserId { get; set; }
        [Required][MaxLength(320)] public string Address { get; set; } = default!;
        [Required][MaxLength(20)] public string Status { get; set; } = "Pending";
        [MaxLength(500)] public string? Error { get; set; }
        public DateTime? SentOn { get; set; }
        /// <summary>True for rows created by the test-send endpoint (excluded from campaign counts).</summary>
        public bool IsTest { get; set; }
    }
}
