using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Branches
{
    public class BranchConfiguration : IEntityTypeConfiguration<Branch>
    {
        public void Configure(EntityTypeBuilder<Branch> b)
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => x.IsDefault);
            b.Property(x => x.CurrencyCode).HasDefaultValue("USD");
            b.Property(x => x.TimeZoneId).HasDefaultValue("Asia/Beirut");
        }
    }
}
