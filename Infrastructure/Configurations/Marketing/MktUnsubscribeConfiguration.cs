using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Marketing
{
    public class MktUnsubscribeConfiguration : IEntityTypeConfiguration<MktUnsubscribe>
    {
        public void Configure(EntityTypeBuilder<MktUnsubscribe> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Address).IsUnique();
        }
    }
}
