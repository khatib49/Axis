using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A partner / owner of the business with an ownership share.
    /// Every owner has their own Drawings sub-account (Equity, contra) under
    /// the "Owners' Drawings" header account, so each owner's cash taken out
    /// is tracked separately and the header rolls up the total.
    /// Backed by a manually-created table; see db-migrations/2026-10-owners-drawings.sql.
    /// </summary>
    [Table("Owners")]
    public class Owner
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = default!;

        /// <summary>Ownership share, 0–100 (e.g. 50.00 = 50%).</summary>
        [Precision(5, 2)]
        public decimal OwnershipPercent { get; set; }

        /// <summary>The owner's Drawings account (Equity, child of the Owners' Drawings header).</summary>
        public int DrawingsAccountId { get; set; }
        public Account DrawingsAccount { get; set; } = default!;

        [MaxLength(500)]
        public string? Notes { get; set; }

        // Soft-delete. Owners are never hard-deleted because their drawings
        // history (and the journal entries behind it) must stay intact.
        public bool IsActive { get; set; } = true;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? ModifiedOn { get; set; }

        public ICollection<OwnerDrawing> Drawings { get; set; } = new List<OwnerDrawing>();
    }
}
