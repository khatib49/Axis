namespace Application.IServices
{
    public interface ISiteContentService
    {
        /// <summary>Stored JSON document for the key, or null when nothing was saved yet.</summary>
        Task<string?> GetJsonAsync(string key, CancellationToken ct = default);

        /// <summary>
        /// Document plus a version tag for HTTP caching (ETag). Served from memory;
        /// the database is only read on first use and after a save.
        /// </summary>
        Task<(string Json, string ETag)> GetWithEtagAsync(string key, CancellationToken ct = default);

        /// <summary>Creates or replaces the document.</summary>
        Task SaveJsonAsync(string key, string json, string? actor, CancellationToken ct = default);
    }
}
