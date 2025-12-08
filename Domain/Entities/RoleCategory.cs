namespace Domain.Entities
{
    /// <summary>
    /// Maps kitchen roles to specific categories they can see
    /// Admin configures which categories each role (chef/bartender) can access
    /// </summary>
    public class RoleCategory
    {
        public int Id { get; set; }

        /// <summary>
        /// Role name: "chef", "bartender", etc.
        /// </summary>
        public string RoleName { get; set; } = string.Empty;

        /// <summary>
        /// Category ID that this role can see
        /// </summary>
        public int CategoryId { get; set; }
        public Category Category { get; set; } = default!;

        /// <summary>
        /// Whether this mapping is active
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Audit fields
        /// </summary>
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; } = string.Empty;
        public DateTime? ModifiedOn { get; set; }
        public string? ModifiedBy { get; set; }
    }
}