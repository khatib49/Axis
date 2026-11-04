using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// One physical unit of a serialised item. Received → InStock; a sale
    /// marks it Sold against the invoice (TransactionId); a return moves it
    /// to Returned (invoice kept for the audit trail); Faulty units are
    /// taken out of the sellable pool. Stock quantities on Item / ItemVariant
    /// are NOT touched by this table — purchases and sales keep doing that;
    /// the serial summary endpoint shows the variance between the two.
    /// </summary>
    [Table("Retail_SerialNumbers")]
    public class SerialNumber
    {
        [Key] public int Id { get; set; }

        public int ItemId { get; set; }
        public int? VariantId { get; set; }

        [Required][MaxLength(80)] public string Serial { get; set; } = default!;
        /// <summary>InStock | Sold | Returned | Faulty</summary>
        [Required][MaxLength(16)] public string Status { get; set; } = SerialStatus.InStock;

        /// <summary>Invoice (transactions.Id) the unit was sold on.</summary>
        public int? TransactionId { get; set; }

        public DateTime ReceivedOn { get; set; } = DateTime.UtcNow;
        public DateTime? SoldOn { get; set; }
        public DateTime? ReturnedOn { get; set; }
        [MaxLength(500)] public string? Notes { get; set; }

        public int? BranchId { get; set; }
        [MaxLength(256)] public string CreatedBy { get; set; } = "system";
        public DateTime? ModifiedOn { get; set; }
    }

    public static class SerialStatus
    {
        public const string InStock = "InStock";
        public const string Sold = "Sold";
        public const string Returned = "Returned";
        public const string Faulty = "Faulty";
        public static readonly string[] All = { InStock, Sold, Returned, Faulty };
    }
}
