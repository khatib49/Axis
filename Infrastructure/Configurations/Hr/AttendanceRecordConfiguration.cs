using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Hr
{
    public class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
    {
        public void Configure(EntityTypeBuilder<AttendanceRecord> b)
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.EmployeeId, e.ClockIn });
            b.HasIndex(e => e.ClockIn);
            b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<ScheduledShift>().WithMany().HasForeignKey(e => e.ScheduledShiftId).OnDelete(DeleteBehavior.SetNull);
        }
    }
}
