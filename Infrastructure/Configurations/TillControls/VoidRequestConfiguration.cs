using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.TillControls
{
    public class VoidRequestConfiguration : IEntityTypeConfiguration<VoidRequest>
    {
        public void Configure(EntityTypeBuilder<VoidRequest> b)
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(10).IsRequired();
            b.Property(x => x.Status).HasMaxLength(10).IsRequired();
            b.Property(x => x.ReasonText).HasMaxLength(1000);
            b.Property(x => x.RequestedBy).HasMaxLength(256).IsRequired();
            b.Property(x => x.DecidedBy).HasMaxLength(256);
            b.Property(x => x.DecisionNote).HasMaxLength(1000);
            b.HasIndex(x => x.TransactionId);
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.BranchId);

            // Referenced by id only: a voided transaction row is deleted by the existing
            // TransactionRecordService, so the request must survive its removal.
            b.HasOne(x => x.Reason).WithMany().HasForeignKey(x => x.ReasonId).OnDelete(DeleteBehavior.Restrict);
        }
    }
}
