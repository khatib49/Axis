using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    /// <summary>
    /// One JSON document per key. The table is created on first use so a
    /// deploy never breaks on a missing migration (the SQL also lives in
    /// db-migrations/2026-09-website-content.sql for the record).
    /// </summary>
    public class SiteContentService : ISiteContentService
    {
        private readonly ApplicationDbContext _db;
        private static bool _tableReady;
        private static readonly SemaphoreSlim Gate = new(1, 1);

        // In-memory copy per key. Refreshed on save (this instance) and every
        // few minutes as a safety net for other instances behind a load balancer.
        private static readonly ConcurrentDictionary<string, (string Json, string ETag, DateTime At)> Cache = new();
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

        public SiteContentService(ApplicationDbContext db) => _db = db;

        public async Task<string?> GetJsonAsync(string key, CancellationToken ct = default)
        {
            var (json, _) = await GetWithEtagAsync(key, ct);
            return string.IsNullOrWhiteSpace(json) || json == "{}" ? null : json;
        }

        public async Task<(string Json, string ETag)> GetWithEtagAsync(string key, CancellationToken ct = default)
        {
            if (Cache.TryGetValue(key, out var hit) && DateTime.UtcNow - hit.At < CacheTtl)
                return (hit.Json, hit.ETag);

            await EnsureTableAsync(ct);
            var row = await _db.SiteContents.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == key, ct);
            var json = string.IsNullOrWhiteSpace(row?.Json) ? "{}" : row!.Json;
            var entry = (json, ComputeEtag(json), DateTime.UtcNow);
            Cache[key] = entry;
            return (entry.json, entry.Item2);
        }

        private static string ComputeEtag(string json)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
            return "\"" + Convert.ToHexString(hash, 0, 12).ToLowerInvariant() + "\"";
        }

        public async Task SaveJsonAsync(string key, string json, string? actor, CancellationToken ct = default)
        {
            await EnsureTableAsync(ct);
            var row = await _db.SiteContents.FirstOrDefaultAsync(x => x.Key == key, ct);
            if (row is null)
            {
                row = new SiteContent { Key = key };
                _db.SiteContents.Add(row);
            }
            row.Json = json;
            row.UpdatedBy = actor;
            row.UpdatedOn = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            Cache[key] = (json, ComputeEtag(json), DateTime.UtcNow);
        }

        private async Task EnsureTableAsync(CancellationToken ct)
        {
            if (_tableReady) return;
            await Gate.WaitAsync(ct);
            try
            {
                if (_tableReady) return;
                // ExecuteSqlRaw runs the text through String.Format, so the
                // JSON default's braces must be doubled to survive.
                await _db.Database.ExecuteSqlRawAsync(@"
CREATE TABLE IF NOT EXISTS ""SiteContents"" (
    ""Id""        SERIAL PRIMARY KEY,
    ""Key""       VARCHAR(60)  NOT NULL,
    ""Json""      TEXT         NOT NULL DEFAULT '{{}}',
    ""UpdatedBy"" VARCHAR(200),
    ""UpdatedOn"" TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);
CREATE UNIQUE INDEX IF NOT EXISTS ""IX_SiteContents_Key"" ON ""SiteContents"" (""Key"");", ct);
                _tableReady = true;
            }
            finally { Gate.Release(); }
        }
    }
}
