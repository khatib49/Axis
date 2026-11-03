using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Branches
{
    public class BranchSettingConfiguration : IEntityTypeConfiguration<BranchSetting>
    {
        public void Configure(EntityTypeBuilder<BranchSetting> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.BranchId, x.Key }).IsUnique();
            b.HasOne(x => x.Branch).WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
