using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Bi
{
    public class ScheduledReportConfiguration : IEntityTypeConfiguration<ScheduledReport>
    {
        public void Configure(EntityTypeBuilder<ScheduledReport> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.IsActive, x.RunAtHourUtc });
            b.HasIndex(x => x.BranchId);
        }
    }
}
