namespace Infrastructure.IRepositories
{
    public interface IUnitOfWork
    {
        Task<int> SaveChangesAsync(CancellationToken ct = default);

        // Optional transaction helpers
        Task BeginTransactionAsync(CancellationToken ct = default);
        Task CommitAsync(CancellationToken ct = default);
        Task RollbackAsync(CancellationToken ct = default);

        /// <summary>
        /// Detaches all tracked entities. Call after RollbackAsync, or between
        /// independent units of work in a long-running operation, to avoid stale
        /// in-memory state polluting subsequent saves.
        /// </summary>
        void ResetChangeTracker();
    }
}
