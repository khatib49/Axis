using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Marketing
{
    public class MktCampaignConfiguration : IEntityTypeConfiguration<MktCampaign>
    {
        public void Configure(EntityTypeBuilder<MktCampaign> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.Status, x.ScheduledFor });
            b.HasIndex(x => x.BranchId);
            b.HasOne(x => x.Segment).WithMany().HasForeignKey(x => x.SegmentId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
