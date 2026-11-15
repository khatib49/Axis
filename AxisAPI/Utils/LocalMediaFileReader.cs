using Application.IServices;

namespace AxisAPI.Utils
{
    /// <summary>Reads item photos from wwwroot (or downloads them when the path is a full URL).</summary>
    public class LocalMediaFileReader : IMediaFileReader
    {
        private const long MaxBytes = 15L * 1024 * 1024;
        private readonly IWebHostEnvironment _env;
        private readonly IHttpClientFactory _http;
        public LocalMediaFileReader(IWebHostEnvironment env, IHttpClientFactory http) { _env = env; _http = http; }

        public async Task<(byte[] Bytes, string MediaType)?> ReadAsync(string pathOrUrl, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(pathOrUrl)) return null;
            if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                using var c = _http.CreateClient();
                c.Timeout = TimeSpan.FromSeconds(30);
                using var resp = await c.GetAsync(uri, ct);
                if (!resp.IsSuccessStatusCode) return null;
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                return bytes.LongLength is 0 or > MaxBytes ? null : (bytes, resp.Content.Headers.ContentType?.MediaType ?? TypeOf(uri.AbsolutePath));
            }
            var root = Path.GetFullPath(_env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot"));
            var full = Path.GetFullPath(Path.Combine(root, pathOrUrl.TrimStart('/', '\\')));
            if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full)) return null;   // stays inside wwwroot
            var info = new FileInfo(full);
            if (info.Length is 0 or > MaxBytes) return null;
            return (await File.ReadAllBytesAsync(full, ct), TypeOf(full));
        }

        private static string TypeOf(string path) => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png", ".webp" => "image/webp", ".gif" => "image/gif", _ => "image/jpeg",
        };
    }
}
