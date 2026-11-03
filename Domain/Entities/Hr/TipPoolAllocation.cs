using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Domain.Entities
{
    [Table("Hr_TipPoolAllocations")]
    public class TipPoolAllocation
    {
        [Key]
        public int Id { get; set; }
        public int TipPoolId { get; set; }
        public int EmployeeId { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal? Points { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal? Hours { get; set; }
        [Column(TypeName = "numeric(18,2)")]
        public decimal Amount { get; set; }
    }
}
