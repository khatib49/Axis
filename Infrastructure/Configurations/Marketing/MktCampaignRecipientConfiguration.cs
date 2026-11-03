using Domain.Entities;
using Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Marketing
{
    public class MktCampaignRecipientConfiguration : IEntityTypeConfiguration<MktCampaignRecipient>
    {
        public void Configure(EntityTypeBuilder<MktCampaignRecipient> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.CampaignId, x.Status });
            b.HasOne(x => x.Campaign).WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        }
    }
}
