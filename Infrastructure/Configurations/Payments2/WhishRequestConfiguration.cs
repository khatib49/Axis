using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Payments2
{
    public class WhishRequestConfiguration : IEntityTypeConfiguration<WhishRequest>
    {
        public void Configure(EntityTypeBuilder<WhishRequest> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.ExternalId).IsUnique();
            b.HasIndex(x => x.TransactionId);
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.BranchId);
            b.HasOne<TransactionRecord>().WithMany().HasForeignKey(x => x.TransactionId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
