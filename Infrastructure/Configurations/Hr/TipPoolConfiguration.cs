using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Hr
{
    public class TipPoolConfiguration : IEntityTypeConfiguration<TipPool>
    {
        public void Configure(EntityTypeBuilder<TipPool> b)
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.Date);
            b.HasMany(e => e.Allocations).WithOne().HasForeignKey(a => a.TipPoolId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class TipPoolAllocationConfiguration : IEntityTypeConfiguration<TipPoolAllocation>
    {
        public void Configure(EntityTypeBuilder<TipPoolAllocation> b)
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.TipPoolId, e.EmployeeId }).IsUnique();
            b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
