using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Hr
{
    public class ScheduledShiftConfiguration : IEntityTypeConfiguration<ScheduledShift>
    {
        public void Configure(EntityTypeBuilder<ScheduledShift> b)
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.EmployeeId, e.Date });
            b.HasIndex(e => e.Date);
            b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ShiftTemplate>().WithMany().HasForeignKey(e => e.TemplateId).OnDelete(DeleteBehavior.SetNull);
        }
    }
}
