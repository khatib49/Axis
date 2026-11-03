using Domain.Entities;
using Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.TillControls
{
    public class CashierShiftConfiguration : IEntityTypeConfiguration<CashierShift>
    {
        public void Configure(EntityTypeBuilder<CashierShift> b)
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.RegisterName).HasMaxLength(100).IsRequired();
            b.Property(x => x.UserName).HasMaxLength(256).IsRequired();
            b.Property(x => x.Status).HasMaxLength(20).IsRequired();
            b.Property(x => x.ClosedBy).HasMaxLength(256);
            b.HasIndex(x => new { x.UserId, x.RegisterName, x.Status });
            b.HasIndex(x => x.OpenedOn);
            b.HasIndex(x => x.BranchId);

            b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Movements).WithOne(m => m.Shift).HasForeignKey(m => m.ShiftId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
