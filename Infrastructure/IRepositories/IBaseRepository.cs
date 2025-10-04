using System.Linq.Expressions;

namespace Infrastructure.IRepositories
{
    public interface IBaseRepository<T> where T : class
    {
        // GET
        Task<T?> GetByIdAsync(Guid id, bool asNoTracking = true, CancellationToken ct = default);
        Task<List<T>> ListAsync(Expression<Func<T, bool>>? predicate = null, bool asNoTracking = true, CancellationToken ct = default);

        // For advanced scenarios (paging, includes, projections)
        IQueryable<T> Query(bool asNoTracking = true);

        // CREATE
        Task AddAsync(T entity, CancellationToken ct = default);
        Task AddRangeAsync(IEnumerable<T> entities, CancellationToken ct = default);

        // UPDATE
        void Update(T entity);
        void UpdateRange(IEnumerable<T> entities);

        // DELETE
        void Remove(T entity);
        void RemoveRange(IEnumerable<T> entities);

        // ATTACH (when you have detached objects and want to mark state)
        void Attach(T entity);

        //Count 

        Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null, CancellationToken ct = default);

        //Queryable function

        IQueryable<T> QueryableAsync(Expression<Func<T, bool>>? predicate = null, bool asNoTracking = true);
    }
}
