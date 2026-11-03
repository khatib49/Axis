using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("Till_VoidReasons")]
    public class VoidReason
    {
        public int Id { get; set; }
        public string Name { get; set; } = default!;
        /// <summary>When true a void with this reason waits for an admin decision.</summary>
        public bool RequiresApproval { get; set; } = true;
        public bool IsActive { get; set; } = true;
    }
}
