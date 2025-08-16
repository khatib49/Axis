using Infrastructure.IRepositories;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Storage;

namespace Infrastructure.Repositories
{
    public class UnitOfWork : IUnitOfWork, IAsyncDisposable
    {
        private readonly ApplicationDbContext _db;
        private IDbContextTransaction? _tx;

        public UnitOfWork(ApplicationDbContext db) => _db = db;

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);

        public async Task BeginTransactionAsync(CancellationToken ct = default)
        {
            if (_tx is not null) return;
            _tx = await _db.Database.BeginTransactionAsync(ct);
        }

        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (_tx is null) return;
            await _db.SaveChangesAsync(ct);
            await _tx.CommitAsync(ct);
            await _tx.DisposeAsync();
            _tx = null;
        }

        public async Task RollbackAsync(CancellationToken ct = default)
        {
            if (_tx is null) return;
            await _tx.RollbackAsync(ct);
            await _tx.DisposeAsync();
            _tx = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (_tx is not null) await _tx.DisposeAsync();
        }
    }
}
