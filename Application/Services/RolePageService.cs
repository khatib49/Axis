using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Application.DTOs;
using Application.IServices;
using Application.Security;
using Domain.Entities;
using Domain.Identity;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    /// <summary>
    /// Role → pages mapping. The table is created on first use and seeded
    /// with what each built-in role could already open, plus the
    /// "social_media" role (events + website), so nothing changes for
    /// existing staff until an admin edits a role.
    /// </summary>
    public class RolePageService : IRolePageService
    {
        private const string SocialMediaRole = "social_media";
        private static readonly Regex RoleNameRx = new("^[a-z][a-z0-9_]{1,39}$", RegexOptions.Compiled);
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

        private static bool _ready;
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly ConcurrentDictionary<string, (DateTime at, HashSet<string> pages)> Cache = new(StringComparer.OrdinalIgnoreCase);

        private readonly ApplicationDbContext _db;
        private readonly RoleManager<AppRole> _roles;
        private readonly UserManager<AppUser> _users;

        public RolePageService(ApplicationDbContext db, RoleManager<AppRole> roles, UserManager<AppUser> users)
        {
            _db = db;
            _roles = roles;
            _users = users;
        }

        public IReadOnlyList<PageDto> Catalog() =>
            PageCatalog.Pages.Select(p => new PageDto(p.Key, p.Label, p.Group, p.Path)).ToList();

        public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct = default)
        {
            await EnsureReadyAsync(ct);
            var names = await _roles.Roles.AsNoTracking()
                .Select(r => r.Name!)
                .OrderBy(n => n)
                .ToListAsync(ct);

            var grants = await _db.RolePages.AsNoTracking().ToListAsync(ct);
            var result = new List<RoleDto>();
            foreach (var name in names)
            {
                var users = (await _users.GetUsersInRoleAsync(name)).Count;
                result.Add(ToDto(name, users, grants));
            }
            // Admin first, then built-ins, then custom roles.
            return result
                .OrderBy(r => r.Name == PageCatalog.AdminRole ? 0 : r.BuiltIn ? 1 : 2)
                .ThenBy(r => r.Name)
                .ToList();
        }

        public async Task<RoleDto> CreateAsync(string name, IReadOnlyList<string>? pages, CancellationToken ct = default)
        {
            await EnsureReadyAsync(ct);
            name = Normalize(name);
            if (!RoleNameRx.IsMatch(name))
                throw new ArgumentException("Role name: 2–40 characters, lower-case letters, digits and underscores, starting with a letter.");
            if (await _roles.RoleExistsAsync(name))
                throw new InvalidOperationException($"Role '{name}' already exists.");

            var created = await _roles.CreateAsync(new AppRole { Name = name });
            if (!created.Succeeded)
                throw new InvalidOperationException(string.Join("; ", created.Errors.Select(e => e.Description)));

            await ReplacePagesAsync(name, pages ?? Array.Empty<string>(), ct);
            return ToDto(name, 0, await _db.RolePages.AsNoTracking().Where(x => x.RoleName == name).ToListAsync(ct));
        }

        public async Task<RoleDto> SetPagesAsync(string name, IReadOnlyList<string> pages, CancellationToken ct = default)
        {
            await EnsureReadyAsync(ct);
            name = Normalize(name);
            if (name == PageCatalog.AdminRole)
                throw new InvalidOperationException("The admin role always has every page.");
            if (!await _roles.RoleExistsAsync(name))
                throw new KeyNotFoundException($"Role '{name}' does not exist.");

            await ReplacePagesAsync(name, pages, ct);
            var users = (await _users.GetUsersInRoleAsync(name)).Count;
            return ToDto(name, users, await _db.RolePages.AsNoTracking().Where(x => x.RoleName == name).ToListAsync(ct));
        }

        public async Task DeleteAsync(string name, CancellationToken ct = default)
        {
            await EnsureReadyAsync(ct);
            name = Normalize(name);
            if (PageCatalog.BuiltInRoles.Contains(name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"'{name}' is a built-in role and can't be deleted.");
            var role = await _roles.FindByNameAsync(name)
                       ?? throw new KeyNotFoundException($"Role '{name}' does not exist.");
            var holders = await _users.GetUsersInRoleAsync(name);
            if (holders.Count > 0)
                throw new InvalidOperationException($"{holders.Count} user(s) still have the '{name}' role. Reassign them first.");

            var deleted = await _roles.DeleteAsync(role);
            if (!deleted.Succeeded)
                throw new InvalidOperationException(string.Join("; ", deleted.Errors.Select(e => e.Description)));

            await _db.RolePages.Where(x => x.RoleName == name).ExecuteDeleteAsync(ct);
            Cache.TryRemove(name, out _);
        }

        public async Task<IReadOnlyCollection<string>> GetPagesForRolesAsync(IEnumerable<string> roles, CancellationToken ct = default)
        {
            var list = roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(Normalize).Distinct().ToList();
            if (list.Count == 0) return Array.Empty<string>();
            if (list.Contains(PageCatalog.AdminRole)) return PageCatalog.Keys;

            await EnsureReadyAsync(ct);
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.UtcNow;
            var missing = new List<string>();
            foreach (var role in list)
            {
                if (Cache.TryGetValue(role, out var entry) && now - entry.at < CacheTtl)
                    result.UnionWith(entry.pages);
                else
                    missing.Add(role);
            }
            if (missing.Count > 0)
            {
                var rows = await _db.RolePages.AsNoTracking()
                    .Where(x => missing.Contains(x.RoleName))
                    .ToListAsync(ct);
                foreach (var role in missing)
                {
                    var pages = rows.Where(r => r.RoleName == role).Select(r => r.PageKey)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    Cache[role] = (now, pages);
                    result.UnionWith(pages);
                }
            }
            return result;
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private static string Normalize(string name) => (name ?? string.Empty).Trim().ToLowerInvariant();

        private static RoleDto ToDto(string name, int users, List<RolePage> grants)
        {
            var isAdmin = name == PageCatalog.AdminRole;
            var pages = isAdmin
                ? PageCatalog.Pages.Select(p => p.Key).ToList()
                : grants.Where(g => g.RoleName == name).Select(g => g.PageKey).OrderBy(k => k).ToList();
            return new RoleDto(name, PageCatalog.BuiltInRoles.Contains(name, StringComparer.OrdinalIgnoreCase), users, pages);
        }

        private async Task ReplacePagesAsync(string role, IReadOnlyList<string> pages, CancellationToken ct)
        {
            var keys = pages.Select(p => (p ?? string.Empty).Trim().ToLowerInvariant())
                .Where(PageCatalog.IsKnown)
                .Distinct()
                .ToList();

            await _db.RolePages.Where(x => x.RoleName == role).ExecuteDeleteAsync(ct);
            _db.RolePages.AddRange(keys.Select(k => new RolePage { RoleName = role, PageKey = k }));
            await _db.SaveChangesAsync(ct);
            Cache.TryRemove(role, out _);
        }

        private async Task EnsureReadyAsync(CancellationToken ct)
        {
            if (_ready) return;
            await Gate.WaitAsync(ct);
            try
            {
                if (_ready) return;
                await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""RolePages"" (
    ""Id""       SERIAL PRIMARY KEY,
    ""RoleName"" VARCHAR(60) NOT NULL,
    ""PageKey""  VARCHAR(60) NOT NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_RolePages_Role_Page"" ON ""RolePages"" (""RoleName"", ""PageKey"");", ct);

                // First run: give every built-in role what it could already open.
                if (!await _db.RolePages.AnyAsync(ct))
                {
                    foreach (var role in PageCatalog.BuiltInRoles.Where(r => r != PageCatalog.AdminRole && r != SocialMediaRole))
                        _db.RolePages.AddRange(PageCatalog.LegacyPagesFor(role).Select(k => new RolePage { RoleName = role, PageKey = k }));
                    _db.RolePages.AddRange(PageCatalog.SocialMediaPages.Select(k => new RolePage { RoleName = SocialMediaRole, PageKey = k }));
                    await _db.SaveChangesAsync(ct);
                }

                if (!await _roles.RoleExistsAsync(SocialMediaRole))
                    await _roles.CreateAsync(new AppRole { Name = SocialMediaRole });

                _ready = true;
            }
            finally { Gate.Release(); }
        }
    }
}
