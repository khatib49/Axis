using Application.Common;
using Application.DTOs;
using Application.IServices;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Phase4
{
    /// <summary>
    /// 3D models per menu item. Files go through IMediaStorageService into
    /// wwwroot/media/models; the header bytes are checked so a renamed JPEG
    /// cannot pose as a model.
    /// </summary>
    public class ItemModel3DService : IItemModel3DService
    {
        private const string Folder = "models";
        private readonly ApplicationDbContext _db;
        private readonly IMediaStorageService _media;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<ItemModel3DService> _log;

        public ItemModel3DService(ApplicationDbContext db, IMediaStorageService media, ICurrentUserAccessor user, ILogger<ItemModel3DService> log)
        { _db = db; _media = media; _user = user; _log = log; }

        public async Task<List<ItemModel3DDto>> ListAsync(CancellationToken ct = default)
            => await (from m in _db.Set<ItemModel3D>().AsNoTracking()
                      join i in _db.Set<Item>().AsNoTracking() on m.ItemId equals i.Id
                      orderby i.Name
                      select new ItemModel3DDto(m.Id, m.ItemId, i.Name, m.GlbPath, m.UsdzPath, m.PosterPath, m.GlbBytes, m.IsActive, m.UpdatedOn ?? m.CreatedOn, m.UpdatedBy))
                .ToListAsync(ct);

        public async Task<List<PublicItemModelDto>> ListPublicAsync(CancellationToken ct = default)
            => await (from m in _db.Set<ItemModel3D>().AsNoTracking()
                      join i in _db.Set<Item>().AsNoTracking() on m.ItemId equals i.Id
                      where m.IsActive && i.StatusId == 1
                      select new PublicItemModelDto(m.ItemId, m.GlbPath, m.UsdzPath, m.PosterPath))
                .ToListAsync(ct);

        public async Task<BaseResponse<ItemModel3DDto>> UploadAsync(int itemId, IFormFile? glb, IFormFile? usdz, IFormFile? poster, CancellationToken ct = default)
        {
            if (!await _db.Set<Item>().AnyAsync(i => i.Id == itemId, ct)) return Fail("NotFound", $"Item #{itemId} not found.");
            var row = await _db.Set<ItemModel3D>().FirstOrDefaultAsync(m => m.ItemId == itemId, ct);
            if (row is null && glb is null) return Fail("Validation", "A .glb file is required for the first upload.");
            if (glb is null && usdz is null && poster is null) return Fail("Validation", "Send at least one file (glb, usdz or poster).");

            if (glb is not null && (!HasExt(glb, ".glb") || !await StartsWithAsync(glb, "glTF"u8.ToArray(), ct)))
                return Fail("Validation", "The glb file is not a binary glTF (.glb) model.");
            if (usdz is not null && (!HasExt(usdz, ".usdz") || !await StartsWithAsync(usdz, new byte[] { 0x50, 0x4B, 0x03, 0x04 }, ct)))
                return Fail("Validation", "The usdz file is not a valid USDZ package.");

            var toDelete = new List<string?>();
            string? newGlb = null, newUsdz = null, newPoster = null;
            try
            {
                if (glb is not null) newGlb = await _media.SaveAsync(glb, Folder, MediaKind.Model3D, ct);
                if (usdz is not null) newUsdz = await _media.SaveAsync(usdz, Folder, MediaKind.Model3D, ct);
                if (poster is not null) newPoster = await _media.SaveAsync(poster, Folder, MediaKind.Image, ct);
            }
            catch (ArgumentException ex)
            {
                await _media.DeleteAsync(newGlb); await _media.DeleteAsync(newUsdz); await _media.DeleteAsync(newPoster);
                return Fail("Validation", ex.Message);
            }

            var now = DateTime.UtcNow;
            if (row is null)
            {
                row = new ItemModel3D { ItemId = itemId, GlbPath = newGlb!, GlbBytes = glb!.Length, UsdzPath = newUsdz, PosterPath = newPoster, CreatedOn = now, UpdatedBy = _user.UserName };
                _db.Add(row);
            }
            else
            {
                if (newGlb is not null) { toDelete.Add(row.GlbPath); row.GlbPath = newGlb; row.GlbBytes = glb!.Length; }
                if (newUsdz is not null) { toDelete.Add(row.UsdzPath); row.UsdzPath = newUsdz; }
                if (newPoster is not null) { toDelete.Add(row.PosterPath); row.PosterPath = newPoster; }
                row.UpdatedOn = now; row.UpdatedBy = _user.UserName;
            }
            await _db.SaveChangesAsync(ct);
            foreach (var p in toDelete) await _media.DeleteAsync(p);
            _log.LogInformation("3D model for item {Item} saved by {User}", itemId, _user.UserName);
            return new(true, null, "3D model saved.", (await ListAsync(ct)).First(m => m.ItemId == itemId));
        }

        public async Task<BaseResponse<ItemModel3DDto>> SetActiveAsync(int itemId, bool active, CancellationToken ct = default)
        {
            var row = await _db.Set<ItemModel3D>().FirstOrDefaultAsync(m => m.ItemId == itemId, ct);
            if (row is null) return Fail("NotFound", "This item has no 3D model.");
            row.IsActive = active; row.UpdatedOn = DateTime.UtcNow; row.UpdatedBy = _user.UserName;
            await _db.SaveChangesAsync(ct);
            return new(true, null, active ? "3D model shown." : "3D model hidden.", (await ListAsync(ct)).First(m => m.ItemId == itemId));
        }

        public async Task<bool> DeleteAsync(int itemId, CancellationToken ct = default)
        {
            var row = await _db.Set<ItemModel3D>().FirstOrDefaultAsync(m => m.ItemId == itemId, ct);
            if (row is null) return false;
            _db.Remove(row);
            await _db.SaveChangesAsync(ct);
            await _media.DeleteAsync(row.GlbPath); await _media.DeleteAsync(row.UsdzPath); await _media.DeleteAsync(row.PosterPath);
            return true;
        }

        private static bool HasExt(IFormFile f, string ext) => string.Equals(Path.GetExtension(f.FileName), ext, StringComparison.OrdinalIgnoreCase);

        private static async Task<bool> StartsWithAsync(IFormFile f, byte[] magic, CancellationToken ct)
        {
            if (f.Length < magic.Length) return false;
            var buf = new byte[magic.Length];
            await using var s = f.OpenReadStream();
            var read = 0;
            while (read < buf.Length)
            {
                var n = await s.ReadAsync(buf.AsMemory(read), ct);
                if (n == 0) break;
                read += n;
            }
            return read == buf.Length && buf.AsSpan().SequenceEqual(magic);
        }

        private static BaseResponse<ItemModel3DDto> Fail(string e, string m) => new(false, e, m);
    }
}
