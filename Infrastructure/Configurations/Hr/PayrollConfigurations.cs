using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Hr
{
    public class PayComponentTypeConfiguration : IEntityTypeConfiguration<PayComponentType>
    {
        public void Configure(EntityTypeBuilder<PayComponentType> b)
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => e.Name).IsUnique();
        }
    }

    public class EmployeePayComponentConfiguration : IEntityTypeConfiguration<EmployeePayComponent>
    {
        public void Configure(EntityTypeBuilder<EmployeePayComponent> b)
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.EmployeeId, e.ComponentTypeId }).IsUnique();
            b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<PayComponentType>().WithMany().HasForeignKey(e => e.ComponentTypeId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
    {
        public void Configure(EntityTypeBuilder<PayrollRun> b)
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.Year, e.Month }).IsUnique();
            b.HasMany(e => e.Lines).WithOne().HasForeignKey(l => l.RunId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<JournalEntry>().WithMany().HasForeignKey(e => e.JournalEntryId).OnDelete(DeleteBehavior.SetNull);
            b.HasOne<JournalEntry>().WithMany().HasForeignKey(e => e.PaymentJournalEntryId).OnDelete(DeleteBehavior.SetNull);
        }
    }

    public class PayrollLineConfiguration : IEntityTypeConfiguration<PayrollLine>
    {
        public void Configure(EntityTypeBuilder<PayrollLine> b)
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.RunId, e.EmployeeId }).IsUnique();
            b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class PayrollAdvanceConfiguration : IEntityTypeConfiguration<PayrollAdvance>
    {
        public void Configure(EntityTypeBuilder<PayrollAdvance> b)
        {
            b.HasKey(e => e.Id);
            b.HasIndex(e => new { e.EmployeeId, e.On });
            b.HasOne<Employee>().WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne<PayrollRun>().WithMany().HasForeignKey(e => e.RecoveredInRunId).OnDelete(DeleteBehavior.SetNull);
        }
    }
}
