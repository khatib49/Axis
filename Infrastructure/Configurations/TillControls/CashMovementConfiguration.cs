using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.TillControls
{
    public class CashMovementConfiguration : IEntityTypeConfiguration<CashMovement>
    {
        public void Configure(EntityTypeBuilder<CashMovement> b)
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).HasMaxLength(10).IsRequired();
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.Property(x => x.CreatedBy).HasMaxLength(256).IsRequired();
            b.HasIndex(x => x.ShiftId);
        }
    }
}
