using Infrastructure.IRepositories;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Infrastructure.Repositories
{
    public class BaseRepository<T> : IBaseRepository<T> where T : class
    {
        private readonly DbContext _db;

        public BaseRepository(DbContext db) => _db = db;

        public async Task<T?> GetByIdAsync(Guid id, bool asNoTracking = true, CancellationToken ct = default)
        {
            var set = _db.Set<T>();
            if (!asNoTracking) return await set.FindAsync([id], ct);
            var entity = await set.FindAsync([id], ct);
            if (entity is not null) _db.Entry(entity).State = EntityState.Detached;
            return entity;
        }

        public async Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, bool asNoTracking = true, CancellationToken ct = default)
        {
            IQueryable<T> q = _db.Set<T>();
            if (asNoTracking) q = q.AsNoTracking();
            if (predicate is not null) q = q.Where(predicate);
            return await q.ToListAsync(ct);
        }

        public IQueryable<T> Query(bool asNoTracking = true)
        {
            var q = _db.Set<T>().AsQueryable();
            return asNoTracking ? q.AsNoTracking() : q;
        }

        public Task AddAsync(T entity, CancellationToken ct = default) =>
            _db.Set<T>().AddAsync(entity, ct).AsTask();

        public Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default) =>
            _db.Set<T>().AddRangeAsync(entities, ct);

        public void Update(T entity) => _db.Set<T>().Update(entity);
        public void UpdateRange(IEnumerable<T> entities) => _db.Set<T>().UpdateRange(entities);

        public void Remove(T entity) => _db.Set<T>().Remove(entity);
        public void RemoveRange(IEnumerable<T> entities) => _db.Set<T>().RemoveRange(entities);

        public void Attach(T entity) => _db.Set<T>().Attach(entity);
    }
}
