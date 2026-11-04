using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// A scannable code attached to an item, or to one of its colour / type
    /// variants. An item (or variant) can carry several codes (supplier
    /// EAN, in-house label…) but only one is the PRIMARY code that gets
    /// printed on labels. Symbology is informational — the till looks the
    /// code up by its exact normalised text whatever the symbology.
    /// </summary>
    [Table("Retail_Barcodes")]
    public class ItemBarcode
    {
        [Key] public int Id { get; set; }

        public int ItemId { get; set; }
        /// <summary>Null = the code identifies the plain item; set = this exact option.</summary>
        public int? VariantId { get; set; }

        [Required][MaxLength(64)] public string Code { get; set; } = default!;
        /// <summary>EAN13 | CODE128 | QR | OTHER</summary>
        [Required][MaxLength(16)] public string Symbology { get; set; } = BarcodeSymbology.Code128;

        public bool IsPrimary { get; set; }

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        [MaxLength(256)] public string CreatedBy { get; set; } = "system";
    }

    public static class BarcodeSymbology
    {
        public const string Ean13 = "EAN13";
        public const string Code128 = "CODE128";
        public const string Qr = "QR";
        public const string Other = "OTHER";
        public static readonly string[] All = { Ean13, Code128, Qr, Other };
    }
}
