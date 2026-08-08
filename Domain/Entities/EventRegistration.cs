using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A public event sign-up (e.g. "Squid Game X AXIS"). Created from the
    /// anonymous landing page; payment is either auto-confirmed by a gateway
    /// callback (Stripe / Whish) or manually confirmed by an admin (Cash).
    /// </summary>
    [Table("EventRegistrations")]
    public class EventRegistration
    {
        [Key] public int Id { get; set; }

        [Required][MaxLength(60)] public string EventKey { get; set; } = "squid-game-x-axis";

        /// <summary>FK to the Events row. Nullable for rows created before
        /// events became dynamic; EventKey remains the human-readable link.</summary>
        public int? EventId { get; set; }
        [ForeignKey(nameof(EventId))] public Event? Event { get; set; }

        [Required][MaxLength(100)] public string FirstName { get; set; } = default!;
        [Required][MaxLength(100)] public string LastName { get; set; } = default!;
        [Required][MaxLength(40)]  public string Phone { get; set; } = default!;
        [MaxLength(200)] public string? Email { get; set; }

        // 'Visa' | 'Whish' | 'Cash'
        [Required][MaxLength(20)] public string PaymentMethod { get; set; } = default!;

        // 'Pending' | 'Paid' | 'Rejected' | 'Refunded'
        [Required][MaxLength(20)] public string PaymentStatus { get; set; } = "Pending";

        [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }
        [Required][MaxLength(10)] public string Currency { get; set; } = "USD";

        // Stripe session id / Whish externalId — used to match callbacks
        // back to the row that started the payment.
        [MaxLength(200)] public string? ProviderRef { get; set; }
        public string? ProviderPayload { get; set; }

        [MaxLength(200)] public string? ConfirmedBy { get; set; }
        public DateTime? ConfirmedOn { get; set; }
        public string? AdminNotes { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }
    }
}
