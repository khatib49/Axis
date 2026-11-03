using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Bi
{
    public class ReportRunConfiguration : IEntityTypeConfiguration<ReportRun>
    {
        public void Configure(EntityTypeBuilder<ReportRun> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.ScheduledReportId, x.RanOn });
            b.HasOne(x => x.ScheduledReport).WithMany().HasForeignKey(x => x.ScheduledReportId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
