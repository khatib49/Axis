using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.OrderingChannels
{
    public class SelfOrderTableConfiguration : IEntityTypeConfiguration<SelfOrderTable>
    {
        public void Configure(EntityTypeBuilder<SelfOrderTable> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => x.BranchId);
        }
    }
}
