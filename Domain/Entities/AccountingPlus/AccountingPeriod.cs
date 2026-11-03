using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>A calendar month that can be locked against new postings.</summary>
    [Table("Acct_AccountingPeriods")]
    public class AccountingPeriod
    {
        [Key]
        public int Id { get; set; }

        public int Year { get; set; }
        public int Month { get; set; }

        public bool IsLocked { get; set; }

        public DateTime? LockedOn { get; set; }

        [MaxLength(256)]
        public string? LockedBy { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }
}
