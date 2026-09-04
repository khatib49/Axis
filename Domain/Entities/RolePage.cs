using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// One dashboard page a role may open (and the API calls that page needs).
    /// Edited under Admin → Roles &amp; Permissions. The admin role is implicit and
    /// never stored: it can open everything.
    /// </summary>
    [Table("RolePages")]
    public class RolePage
    {
        [Key] public int Id { get; set; }

        /// <summary>Identity role name, lower-case ("cashier", "social_media"…).</summary>
        [Required][MaxLength(60)] public string RoleName { get; set; } = default!;

        /// <summary>Key from the page catalog ("items", "website"…).</summary>
        [Required][MaxLength(60)] public string PageKey { get; set; } = default!;
    }
}
