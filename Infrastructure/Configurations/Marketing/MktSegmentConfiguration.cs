using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Marketing
{
    public class MktSegmentConfiguration : IEntityTypeConfiguration<MktSegment>
    {
        public void Configure(EntityTypeBuilder<MktSegment> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.IsActive);
        }
    }
}
