using System.Security.Claims;
using Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Infrastructure.Persistence
{
    /// <summary>
    /// Stamps <see cref="TransactionRecord.BranchId"/> on every transaction that is
    /// inserted without one, so sales get a branch dimension without touching the
    /// many call sites that create transactions. Resolution order:
    /// <list type="number">
    ///   <item>the <c>X-Branch-Id</c> request header (must name an existing branch);</item>
    ///   <item>the caller's default <see cref="UserBranch"/> (IsDefault first, else the first assignment);</item>
    ///   <item>the system default <see cref="Branch"/> (IsDefault, else the first active one).</item>
    /// </list>
    /// The result is cached for the lifetime of the interceptor (one per request /
    /// job scope). A transaction that already carries a BranchId is left alone.
    /// The interceptor never throws: any failure is logged and the rows are saved
    /// with BranchId = null (reporting attributes them to the default branch).
    /// Registered in Infrastructure/DependencyInjection.cs next to AdminAuditInterceptor.
    /// </summary>
    public class BranchStampInterceptor : SaveChangesInterceptor
    {
        public const string BranchHeader = "X-Branch-Id";

        private readonly IHttpContextAccessor _http;
        private readonly ILogger<BranchStampInterceptor> _logger;

        // Per-scope cache: resolved once per request (or background job scope).
        private bool _resolved;
        private int? _branchId;

        public BranchStampInterceptor(IHttpContextAccessor http, ILogger<BranchStampInterceptor>? logger = null)
        {
            _http = http;
            _logger = logger ?? NullLogger<BranchStampInterceptor>.Instance;
        }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            if (eventData.Context is { } ctx) Stamp(ctx);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context is { } ctx) Stamp(ctx);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void Stamp(DbContext ctx)
        {
            try
            {
                var pending = ctx.ChangeTracker.Entries<TransactionRecord>()
                    .Where(e => e.State == EntityState.Added && e.Entity.BranchId == null)
                    .Select(e => e.Entity)
                    .ToList();
                if (pending.Count == 0) return;

                var branchId = Resolve(ctx);
                if (branchId is null) return;
                foreach (var t in pending) t.BranchId = branchId;
            }
            catch (Exception ex)
            {
                // Never block a sale because the branch could not be resolved.
                _logger.LogWarning(ex, "BranchStampInterceptor could not stamp BranchId; rows are saved without a branch");
            }
        }

        private int? Resolve(DbContext ctx)
        {
            if (_resolved) return _branchId;
            _resolved = true;
            _branchId = FromHeader(ctx) ?? FromUserDefault(ctx) ?? FromSystemDefault(ctx);
            return _branchId;
        }

        private int? FromHeader(DbContext ctx)
        {
            var raw = _http.HttpContext?.Request?.Headers[BranchHeader].FirstOrDefault();
            if (!int.TryParse(raw, out var id) || id <= 0) return null;
            return ctx.Set<Branch>().AsNoTracking().Any(b => b.Id == id) ? id : null;
        }

        private int? FromUserDefault(DbContext ctx)
        {
            var user = _http.HttpContext?.User;
            var raw = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? user?.FindFirstValue("sub");
            if (!int.TryParse(raw, out var userId)) return null;
            return ctx.Set<UserBranch>().AsNoTracking()
                .Where(ub => ub.UserId == userId)
                .OrderByDescending(ub => ub.IsDefault).ThenBy(ub => ub.Id)
                .Select(ub => (int?)ub.BranchId)
                .FirstOrDefault();
        }

        private static int? FromSystemDefault(DbContext ctx) =>
            ctx.Set<Branch>().AsNoTracking()
                .Where(b => b.IsActive)
                .OrderByDescending(b => b.IsDefault).ThenBy(b => b.Id)
                .Select(b => (int?)b.Id)
                .FirstOrDefault();
    }
}
