using Application.Services;

namespace AxisAPI.Utils
{
    /// <summary>
    /// Writes event media into wwwroot/media/{subfolder}/ so it's served as
    /// a static file by the API host. Guards on extension and size — an
    /// unbounded upload endpoint is an easy way to fill a disk.
    /// </summary>
    public class LocalMediaStorageService : IMediaStorageService
    {
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<LocalMediaStorageService> _logger;

        // Kept deliberately tight. Anything a browser can't play natively
        // is a support ticket waiting to happen.
        private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".webm", ".mov", ".m4v" };
        private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

        private const long MaxVideoBytes = 200L * 1024 * 1024; // 200 MB
        private const long MaxImageBytes = 10L * 1024 * 1024;  // 10 MB

        public LocalMediaStorageService(IWebHostEnvironment env, ILogger<LocalMediaStorageService> logger)
        {
            _env = env;
            _logger = logger;
        }

        public async Task<string> SaveAsync(IFormFile file, string subfolder, MediaKind kind, CancellationToken ct = default)
        {
            if (file is null || file.Length == 0)
                throw new ArgumentException("No file was uploaded.");

            var ext = Path.GetExtension(file.FileName);
            var allowed = kind == MediaKind.Video ? VideoExt : ImageExt;
            if (string.IsNullOrWhiteSpace(ext) || !allowed.Contains(ext))
                throw new ArgumentException(
                    $"Unsupported file type '{ext}'. Allowed: {string.Join(", ", allowed)}");

            var maxBytes = kind == MediaKind.Video ? MaxVideoBytes : MaxImageBytes;
            if (file.Length > maxBytes)
                throw new ArgumentException(
                    $"File is too large ({file.Length / 1024 / 1024} MB). Maximum is {maxBytes / 1024 / 1024} MB.");

            // WebRootPath is null when wwwroot doesn't exist yet on a fresh
            // deploy — fall back to ContentRoot/wwwroot and create it.
            var webRoot = _env.WebRootPath;
            if (string.IsNullOrWhiteSpace(webRoot))
            {
                webRoot = Path.Combine(_env.ContentRootPath, "wwwroot");
                Directory.CreateDirectory(webRoot);
            }

            var safeSub = string.Concat((subfolder ?? "misc")
                .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
            if (string.IsNullOrWhiteSpace(safeSub)) safeSub = "misc";

            var folder = Path.Combine(webRoot, "media", safeSub);
            Directory.CreateDirectory(folder);

            var fileName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
            var fullPath = Path.Combine(folder, fileName);

            await using (var stream = new FileStream(fullPath, FileMode.Create))
                await file.CopyToAsync(stream, ct);

            var rel = Path.Combine("media", safeSub, fileName).Replace("\\", "/");
            _logger.LogInformation("Saved {Kind} upload ({Size} KB) to {Path}",
                kind, file.Length / 1024, rel);
            return rel;
        }

        public Task DeleteAsync(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return Task.CompletedTask;

            var webRoot = _env.WebRootPath ?? Path.Combine(_env.ContentRootPath, "wwwroot");
            // Defensive: never let a stored path escape wwwroot.
            var full = Path.GetFullPath(Path.Combine(webRoot, relativePath));
            if (!full.StartsWith(Path.GetFullPath(webRoot), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Refused to delete path outside wwwroot: {Path}", relativePath);
                return Task.CompletedTask;
            }

            if (File.Exists(full)) File.Delete(full);
            return Task.CompletedTask;
        }
    }
}
