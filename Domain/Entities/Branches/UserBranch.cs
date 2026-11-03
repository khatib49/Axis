using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>Assignment of an existing AppUser to a branch.</summary>
    [Table("Branch_UserBranches")]
    public class UserBranch
    {
        public int Id { get; set; }
        /// <summary>Existing AppUser id (Identity). FK configured from this side only.</summary>
        public int UserId { get; set; }
        public int BranchId { get; set; }
        public Branch Branch { get; set; } = default!;
        public bool IsDefault { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
    }
}
