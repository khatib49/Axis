using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// What the customer-facing screen of one register is showing right now.
    /// The till pushes the cart; the screen (holding Token) polls the view.
    /// </summary>
    [Table("Till_DisplayStates")]
    public class DisplayState
    {
        [Key] public int Id { get; set; }
        [Required][MaxLength(60)] public string RegisterName { get; set; } = default!;
        /// <summary>Bearer the display screen holds (in its URL); rotatable.</summary>
        [Required][MaxLength(64)] public string Token { get; set; } = default!;
        /// <summary>Open invoice currently at the till, when known.</summary>
        public int? TransactionId { get; set; }
        /// <summary>JSON array of {name, qty, price} lines.</summary>
        public string CartJson { get; set; } = "[]";
        [Column(TypeName = "numeric(18,2)")] public decimal TotalUsd { get; set; }
        [Column(TypeName = "numeric(18,6)")] public decimal RateLbp { get; set; }
        [Column(TypeName = "numeric(18,2)")] public decimal TotalLbp { get; set; }
        /// <summary>Idle | Cart | Awaiting | Paid | Thanks</summary>
        [Required][MaxLength(20)] public string Status { get; set; } = DisplayStatuses.Idle;
        /// <summary>Pay_WhishRequests id shown as QR while Awaiting.</summary>
        public int? WhishRequestId { get; set; }
        public DateTime UpdatedOn { get; set; } = DateTime.UtcNow;
        public int? BranchId { get; set; }
    }

    public static class DisplayStatuses
    {
        public const string Idle = "Idle";
        public const string Cart = "Cart";
        public const string Awaiting = "Awaiting";
        public const string Paid = "Paid";
        public const string Thanks = "Thanks";
        public static readonly string[] All = { Idle, Cart, Awaiting, Paid, Thanks };
    }
}
