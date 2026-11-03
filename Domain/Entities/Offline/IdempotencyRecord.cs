using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// One successfully executed offline operation, keyed by the client's
    /// operation id. Replaying the same key returns the stored response
    /// instead of executing the order a second time.
    /// </summary>
    [Table("Offline_IdempotencyRecords")]
    public class IdempotencyRecord
    {
        public int Id { get; set; }

        /// <summary>Client operation id (GUID string). Unique.</summary>
        [MaxLength(64)]
        public string Key { get; set; } = default!;

        /// <summary>Logical endpoint / op type, e.g. "CreateCoffeeShopOrder".</summary>
        [MaxLength(64)]
        public string Endpoint { get; set; } = default!;

        /// <summary>SHA-256 (hex) of type + payload, used to detect a different request reusing a key.</summary>
        [MaxLength(64)]
        public string RequestHash { get; set; } = default!;

        public int ResponseStatus { get; set; }

        /// <summary>JSON of the response returned to the client.</summary>
        public string ResponseBody { get; set; } = default!;

        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;

        [MaxLength(256)]
        public string CreatedBy { get; set; } = default!;

        public int? BranchId { get; set; }
    }
}
