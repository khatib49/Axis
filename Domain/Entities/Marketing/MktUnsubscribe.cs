using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// An address (e-mail or phone) that must never receive marketing again.
    /// Address is unique (normalised lower-case / trimmed); Channel records how it
    /// was reached (Email | Phone). Source: Link | Manual.
    /// </summary>
    [Table("Mkt_Unsubscribes")]
    public class MktUnsubscribe
    {
        public int Id { get; set; }
        [Required][MaxLength(320)] public string Address { get; set; } = default!;
        [Required][MaxLength(20)] public string Channel { get; set; } = "Email";
        public DateTime On { get; set; } = DateTime.UtcNow;
        [MaxLength(40)] public string Source { get; set; } = "Link";
    }
}
