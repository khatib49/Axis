using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    /// <summary>
    /// HR staff profile. Optionally linked to an existing AppUser (UserId) so
    /// the person can see their own schedule / leave; till attendance is by PIN.
    /// PinHash is SHA-256(pin + PinSalt) and is never returned to clients.
    /// </summary>
    [Table("Hr_Employees")]
    public class Employee
    {
        [Key]
        public int Id { get; set; }
        public int? UserId { get; set; }
        [Required, MaxLength(150)]
        public string FullName { get; set; } = default!;
        [MaxLength(50)]
        public string? Phone { get; set; }
        [MaxLength(200)]
        public string? Email { get; set; }
        [MaxLength(100)]
        public string? Position { get; set; }
        [MaxLength(100)]
        public string? Department { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal? HourlyRate { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal? MonthlySalary { get; set; }
        [MaxLength(128)]
        public string? PinHash { get; set; }
        [MaxLength(64)]
        public string? PinSalt { get; set; }
        public DateOnly HiredOn { get; set; }
        public DateOnly? TerminatedOn { get; set; }
        public bool IsActive { get; set; } = true;
        [MaxLength(1000)]
        public string? Notes { get; set; }
        public int? BranchId { get; set; }
        public DateTime CreatedOn { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedOn { get; set; }
    }
}
