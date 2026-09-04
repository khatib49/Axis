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

        public SiteContentService(ApplicationDbContext db) => _db = db;

        public async Task<string?> GetJsonAsync(string key, CancellationToken ct = default)
        {
            await EnsureTableAsync(ct);
            var row = await _db.SiteContents.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Key == key, ct);
            return row?.Json;
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
