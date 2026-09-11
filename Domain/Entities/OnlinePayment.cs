using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// One online payment attempt, whatever it is for and whichever gateway
    /// carries it. This is the single ledger the owner looks at to know
    /// "what did we get paid online, by whom, for what, and did it settle".
    ///
    /// Purpose + ReferenceType/ReferenceId say what the money is for
    /// (an event ticket, a wallet top-up, an open invoice, or a free-form
    /// payment link). Provider* fields are the gateway's identifiers.
    /// </summary>
    [Table("OnlinePayments")]
    public class OnlinePayment
    {
        [Key] public int Id { get; set; }

        /// <summary>Public, unguessable short code used in the pay link: /pay/{Code}.</summary>
        [Required][MaxLength(24)] public string Code { get; set; } = default!;

        /// <summary>"MontyPay" today; Stripe / Whish can be folded in later.</summary>
        [Required][MaxLength(30)] public string Provider { get; set; } = default!;

        /// <summary>"sandbox" | "production" — the environment the session was created in.</summary>
        [Required][MaxLength(15)] public string Environment { get; set; } = "sandbox";

        /// <summary>"EventTicket" | "WalletTopUp" | "Invoice" | "Custom".</summary>
        [Required][MaxLength(30)] public string Purpose { get; set; } = default!;

        /// <summary>What the money is for: e.g. "EventRegistration" / 123, "Wallet" / userId, "TransactionRecord" / invoiceId.</summary>
        [MaxLength(40)] public string? ReferenceType { get; set; }
        public int? ReferenceId { get; set; }

        [Column(TypeName = "numeric(18,2)")] public decimal Amount { get; set; }
        [Required][MaxLength(10)] public string Currency { get; set; } = "USD";
        [Required][MaxLength(255)] public string Description { get; set; } = default!;

        [MaxLength(150)] public string? CustomerName { get; set; }
        [MaxLength(40)] public string? CustomerPhone { get; set; }
        [MaxLength(200)] public string? CustomerEmail { get; set; }
        /// <summary>App user (client) when known — lets us show it on the wallet / client page.</summary>
        public int? UserId { get; set; }

        /// <summary>
        /// Created → Redirected (session made, customer sent to gateway) →
        /// Paid | Failed | Cancelled | Expired; Paid may later become Refunded | Voided | Chargeback.
        /// </summary>
        [Required][MaxLength(20)] public string Status { get; set; } = "Created";

        /// <summary>order.number we sent to the gateway — unique per attempt.</summary>
        [MaxLength(64)] public string? ProviderOrderNumber { get; set; }
        /// <summary>Gateway's own payment id (callback `id`).</summary>
        [MaxLength(64)] public string? ProviderPaymentId { get; set; }
        /// <summary>Raw gateway order_status of the last callback (settled / decline / refund …).</summary>
        [MaxLength(30)] public string? ProviderStatus { get; set; }
        /// <summary>Hosted checkout page the customer was sent to.</summary>
        [MaxLength(1000)] public string? RedirectUrl { get; set; }
        [MaxLength(60)] public string? PaymentMethodUsed { get; set; }   // card / applepay / …
        [MaxLength(40)] public string? CardMasked { get; set; }
        [MaxLength(500)] public string? FailureReason { get; set; }

        /// <summary>True once the business effect (ticket confirmed, wallet credited…) has been applied.</summary>
        public bool IsFulfilled { get; set; }
        public DateTime? FulfilledOn { get; set; }
        [MaxLength(500)] public string? FulfillmentError { get; set; }

        public int CallbackCount { get; set; }
        public DateTime? LastCallbackOn { get; set; }

        [MaxLength(200)] public string? CreatedBy { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? PaidOn { get; set; }
        public DateTime? ModifiedOn { get; set; }
        /// <summary>Pay links stop working after this (null = never).</summary>
        public DateTime? ExpiresOn { get; set; }

        public ICollection<OnlinePaymentEvent> Events { get; set; } = new List<OnlinePaymentEvent>();
    }

    /// <summary>
    /// Every callback / status change on a payment, verbatim. Cheap insurance
    /// for the day a customer says "I paid" and the gateway says otherwise.
    /// </summary>
    [Table("OnlinePaymentEvents")]
    public class OnlinePaymentEvent
    {
        [Key] public int Id { get; set; }
        public int OnlinePaymentId { get; set; }
        public OnlinePayment Payment { get; set; } = default!;

        /// <summary>"created" | "redirected" | "callback" | "status-check" | "fulfilled" | "manual"</summary>
        [Required][MaxLength(20)] public string Kind { get; set; } = default!;
        /// <summary>Gateway event type (sale / refund / void / 3ds …) when applicable.</summary>
        [MaxLength(30)] public string? ProviderType { get; set; }
        [MaxLength(20)] public string? ProviderStatus { get; set; }
        [MaxLength(30)] public string? OrderStatus { get; set; }
        /// <summary>Status we moved the payment to as a result (null = no change).</summary>
        [MaxLength(20)] public string? ResultStatus { get; set; }
        [MaxLength(500)] public string? Note { get; set; }
        /// <summary>Raw body (form or JSON) as received — capped at 8k.</summary>
        public string? Raw { get; set; }
        public bool HashValid { get; set; } = true;
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
