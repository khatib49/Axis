using Microsoft.AspNetCore.Http;

namespace Application.Services
{
    /// <summary>
    /// Larger-file storage for event media (promo videos, hero images).
    /// Separate from IImageStorageService because the size limits and
    /// allowed types differ significantly — a 200 MB mp4 has nothing in
    /// common with a 2 MB item thumbnail.
    /// </summary>
    public interface IMediaStorageService
    {
        /// <summary>
        /// Saves under wwwroot/media/{subfolder}/ and returns the relative
        /// path to store in the DB. Throws ArgumentException on a rejected
        /// extension or an oversized file.
        /// </summary>
        Task<string> SaveAsync(IFormFile file, string subfolder, MediaKind kind, CancellationToken ct = default);

        Task DeleteAsync(string? relativePath);
    }

    public enum MediaKind
    {
        Video,
        Image,
    }
}
