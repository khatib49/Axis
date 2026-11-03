using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Configurations.Branches
{
    /// <summary>
    /// Phase 2: transactions carry a nullable BranchId. The rest of the
    /// TransactionRecord mapping stays in ApplicationDbContext; this
    /// configuration only adds the index the per-branch queries need.
    /// </summary>
    public class TransactionRecordBranchConfiguration : IEntityTypeConfiguration<TransactionRecord>
    {
        public void Configure(EntityTypeBuilder<TransactionRecord> b)
        {
            b.HasIndex(t => t.BranchId);
        }
    }
}
