using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Hr
{
    public class ShiftTemplateConfiguration : IEntityTypeConfiguration<ShiftTemplate>
    {
        public void Configure(EntityTypeBuilder<ShiftTemplate> b)
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.Name).IsUnique();
        }
    }
}
