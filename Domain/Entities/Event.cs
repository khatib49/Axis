using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// An admin-managed public event page (Squid Game X AXIS, tournaments,
    /// launch nights…). Everything the landing page renders lives here, so
    /// marketing can spin up a new event without a deploy.
    /// </summary>
    [Table("Events")]
    public class Event
    {
        [Key] public int Id { get; set; }

        /// <summary>URL slug — the page is served at /events/{Key}.</summary>
        [Required][MaxLength(80)] public string Key { get; set; } = default!;

        // ── Content ──────────────────────────────────────────────────
        [Required][MaxLength(200)] public string Title { get; set; } = default!;
        [MaxLength(300)] public string? Subtitle { get; set; }
        public string? Description { get; set; }
        public DateTime? EventDate { get; set; }
        [MaxLength(300)] public string? Location { get; set; }

        /// <summary>JSON array of {icon,title,desc} rendered as feature cards.</summary>
        public string? FeaturesJson { get; set; }

        // ── Media ────────────────────────────────────────────────────
        /// <summary>Uploaded video, relative to wwwroot. Takes priority over YouTube.</summary>
        [MaxLength(400)] public string? VideoPath { get; set; }
        [MaxLength(60)]  public string? VideoYoutubeId { get; set; }
        [MaxLength(400)] public string? HeroImagePath { get; set; }

        // ── Pricing ──────────────────────────────────────────────────
        [Column(TypeName = "numeric(18,2)")] public decimal Price { get; set; }
        [Required][MaxLength(10)] public string Currency { get; set; } = "USD";

        // ── Payment method toggles (per event) ───────────────────────
        public bool EnableVisa { get; set; } = true;
        public bool EnableWhish { get; set; } = true;
        public bool EnableCash { get; set; } = true;

        /// <summary>
        /// Plain Whish payment link (the simple product, no merchant API).
        /// Used as a fallback when Whish.Channel/Secret aren't configured:
        /// the buyer pays via this link and confirms on WhatsApp, then an
        /// admin marks it paid — same manual flow as Cash.
        /// Ignored entirely once the Collect API credentials exist.
        /// </summary>
        [MaxLength(500)] public string? WhishPaymentLink { get; set; }

        // ── WhatsApp confirmation ────────────────────────────────────
        [MaxLength(40)] public string? WhatsAppNumber { get; set; }
        /// <summary>
        /// Message body with {{placeholders}}: eventTitle, registrationId,
        /// fullName, firstName, lastName, phone, email, paymentMethod,
        /// amount, currency, eventDate, location.
        /// </summary>
        public string? WhatsAppTemplate { get; set; }

        // ── Publication ──────────────────────────────────────────────
        public bool IsPublished { get; set; }
        public bool IsActive { get; set; } = true;
        /// <summary>Optional hard cap on PAID registrations. Null = unlimited.</summary>
        public int? Capacity { get; set; }

        [MaxLength(200)] public string? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
    }
}
