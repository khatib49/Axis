using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.TillControls
{
    public class VoidReasonConfiguration : IEntityTypeConfiguration<VoidReason>
    {
        public void Configure(EntityTypeBuilder<VoidReason> b)
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.HasIndex(x => x.Name).IsUnique();
        }
    }
}
