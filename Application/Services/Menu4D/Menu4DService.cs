using System.Text.Json;
using System.Text.Json.Nodes;
using Application.Common;
using Application.DTOs;
using Application.IServices;
using Application.Services.Ai;
using Domain.Entities;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Application.Services.Menu4D
{
    /// <summary>
    /// The 4D menu. Two AI helpers, both optional and both reviewed by staff:
    /// 1. Parts: Claude looks at the item's photo and labels what a guest can see
    ///    (bun, patty, cheese, foam…) with a short line taken from the menu text.
    /// 2. Table AR: a photo-to-3D service (Meshy) turns the photo into a GLB/USDZ
    ///    model. New AI models are saved switched off until staff approve them.
    /// Settings: Anthropic.ApiKey, Menu4D.Meshy.ApiKey, Menu4D.Meshy.BaseUrl, Menu4D.MaxParallel.
    /// </summary>
    public class Menu4DService : IMenu4DService
    {
        public const decimal EstimatedCostPerModelUsd = 0.30m;
        private const int MaxParts = 8;
        private static readonly TimeSpan JobTimeout = TimeSpan.FromHours(2);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;
        private readonly IIntegrationSettingsService _settings;
        private readonly ClaudeApiClient _claude;
        private readonly IPhotoTo3DClient _to3d;
        private readonly IMediaFileReader _files;
        private readonly IMediaStorageService _media;
        private readonly ICurrentUserAccessor _user;
        private readonly ILogger<Menu4DService> _log;

        public Menu4DService(ApplicationDbContext db, IIntegrationSettingsService settings, ClaudeApiClient claude, IPhotoTo3DClient to3d,
            IMediaFileReader files, IMediaStorageService media, ICurrentUserAccessor user, ILogger<Menu4DService> log)
        { _db = db; _settings = settings; _claude = claude; _to3d = to3d; _files = files; _media = media; _user = user; _log = log; }

        private static BaseResponse<T> Fail<T>(string error, string message) => new(false, error, message);
        private string Who => string.IsNullOrWhiteSpace(_user.UserName) ? "system" : _user.UserName;

        // ── Reading ──────────────────────────────────────────────────────

        private IQueryable<Item> PhotoItems() => _db.Set<Item>().AsNoTracking().Where(i => i.ImagePath != null && i.ImagePath != "" && i.StatusId == 1);

        public async Task<List<Item4DRowDto>> ListAsync(CancellationToken ct = default)
        {
            // Dishes with a photo, plus dishes that only have an uploaded 3D scan.
            var scanned = _db.Set<ItemModel3D>().Select(m => m.ItemId);
            var items = await _db.Set<Item>().AsNoTracking()
                .Where(i => i.StatusId == 1 && ((i.ImagePath != null && i.ImagePath != "") || scanned.Contains(i.Id)))
                .Select(i => new { i.Id, i.Name, i.ImagePath, i.Type, i.CategoryId }).ToListAsync(ct);
            var cats = await _db.Set<Category>().AsNoTracking().Select(c => new { c.Id, c.Name }).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
            var profiles = await _db.Set<Item4DProfile>().AsNoTracking().ToDictionaryAsync(p => p.ItemId, ct);
            var models = await _db.Set<ItemModel3D>().AsNoTracking().ToDictionaryAsync(m => m.ItemId, ct);
            return items.OrderBy(i => cats.GetValueOrDefault(i.CategoryId) ?? "").ThenBy(i => i.Name)
                .Select(i => Row(i.Id, i.Name, i.ImagePath, cats.GetValueOrDefault(i.CategoryId), i.Type, profiles.GetValueOrDefault(i.Id), models.GetValueOrDefault(i.Id)))
                .ToList();
        }

        private static Item4DRowDto Row(int id, string name, string? image, string? cat, string? desc, Item4DProfile? p, ItemModel3D? m) =>
            new(id, name, image, cat, desc, ParseParts(p?.PartsJson), p?.PartsPublished ?? false, p?.RealSizeCm,
                p?.GenStatus ?? Item4DGenStatus.None, p?.GenProgress ?? 0, p?.GenError, p?.GenFinishedOn,
                m is not null, m?.IsActive ?? false, m is not null && p?.GenStatus == Item4DGenStatus.Done);

        private async Task<Item4DRowDto?> RowAsync(int itemId, CancellationToken ct)
        {
            var i = await _db.Set<Item>().AsNoTracking().Where(x => x.Id == itemId).Select(x => new { x.Id, x.Name, x.ImagePath, x.Type, x.CategoryId }).FirstOrDefaultAsync(ct);
            if (i is null) return null;
            var cat = await _db.Set<Category>().AsNoTracking().Where(c => c.Id == i.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(ct);
            var p = await _db.Set<Item4DProfile>().AsNoTracking().FirstOrDefaultAsync(x => x.ItemId == itemId, ct);
            var m = await _db.Set<ItemModel3D>().AsNoTracking().FirstOrDefaultAsync(x => x.ItemId == itemId, ct);
            return Row(i.Id, i.Name, i.ImagePath, cat, i.Type, p, m);
        }

        public async Task<Menu4DStatusDto> StatusAsync(CancellationToken ct = default)
        {
            var ids = await PhotoItems().Select(i => i.Id).ToListAsync(ct);
            var profiles = await _db.Set<Item4DProfile>().AsNoTracking().Where(p => ids.Contains(p.ItemId)).ToListAsync(ct);
            var withModel = await _db.Set<ItemModel3D>().AsNoTracking().CountAsync(m => ids.Contains(m.ItemId), ct);
            return new Menu4DStatusDto(
                !string.IsNullOrWhiteSpace(await _settings.GetRawAsync("Anthropic.ApiKey", ct)),
                !string.IsNullOrWhiteSpace(await _settings.GetRawAsync("Menu4D.Meshy.ApiKey", ct)),
                ids.Count,
                profiles.Count(p => ParseParts(p.PartsJson).Count > 0),
                profiles.Count(p => p.PartsPublished && ParseParts(p.PartsJson).Count > 0),
                withModel,
                profiles.Count(p => p.GenStatus == Item4DGenStatus.Queued),
                profiles.Count(p => p.GenStatus == Item4DGenStatus.Running),
                profiles.Count(p => p.GenStatus == Item4DGenStatus.Failed),
                EstimatedCostPerModelUsd);
        }

        public async Task<List<Item4DPublicDto>> ListPublicAsync(CancellationToken ct = default)
        {
            var rows = await _db.Set<Item4DProfile>().AsNoTracking()
                .Where(p => (p.PartsPublished && p.PartsJson != null) || p.RealSizeCm != null)
                .Select(p => new { p.ItemId, p.PartsJson, p.PartsPublished, p.RealSizeCm }).ToListAsync(ct);
            return rows.Select(r => new Item4DPublicDto(r.ItemId, r.PartsPublished ? ParseParts(r.PartsJson) : new List<Dish4DPart>(), r.RealSizeCm)).ToList();
        }

        // ── Parts ────────────────────────────────────────────────────────

        public static List<Dish4DPart> ParseParts(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new();
            try { return JsonSerializer.Deserialize<List<Dish4DPart>>(json, Json) ?? new(); }
            catch (JsonException) { return new(); }
        }

        /// <summary>Cleans parts from the AI or the editor: trims text, clamps positions, drops empties.</summary>
        public static List<Dish4DPart> Clean(IEnumerable<Dish4DPart> parts) => parts
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new Dish4DPart(
                Cut(p.Name.Trim(), 40)!,
                Cut(p.Detail?.Trim(), 160),
                Math.Round(Math.Clamp(double.IsFinite(p.X) ? p.X : 50, 0, 100), 1),
                Math.Round(Math.Clamp(double.IsFinite(p.Y) ? p.Y : 50, 0, 100), 1),
                ValidVec(p.Pos3D), ValidVec(p.Normal3D)))
            .Take(MaxParts).ToList();

        private static string? Cut(string? s, int n) => string.IsNullOrEmpty(s) ? null : s.Length > n ? s[..n] : s;

        /// <summary>Accepts model-viewer vectors like "0.01m 0.2m -0.03m" only.</summary>
        private static string? ValidVec(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            var parts = v.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return null;
            foreach (var p in parts)
                if (!double.TryParse(p.EndsWith('m') ? p[..^1] : p, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)) return null;
            return string.Join(' ', parts.Select(p => p.EndsWith('m') ? p : p + "m"));
        }

        private async Task<Item4DProfile> ProfileAsync(int itemId, CancellationToken ct)
        {
            var p = await _db.Set<Item4DProfile>().FirstOrDefaultAsync(x => x.ItemId == itemId, ct);
            if (p is not null) return p;
            p = new Item4DProfile { ItemId = itemId, UpdatedBy = Who };
            _db.Add(p);
            return p;
        }

        public async Task<BaseResponse<Item4DRowDto>> AnalyzeAsync(int itemId, CancellationToken ct = default)
        {
            var item = await _db.Set<Item>().AsNoTracking().Where(i => i.Id == itemId).Select(i => new { i.Id, i.Name, i.Type, i.ImagePath, i.CategoryId }).FirstOrDefaultAsync(ct);
            if (item is null) return Fail<Item4DRowDto>("NotFound", $"Item #{itemId} not found.");
            if (string.IsNullOrWhiteSpace(item.ImagePath)) return Fail<Item4DRowDto>("Validation", "This item has no photo yet.");
            var apiKey = await _settings.GetRawAsync("Anthropic.ApiKey", ct);
            if (string.IsNullOrWhiteSpace(apiKey)) return Fail<Item4DRowDto>("NotConfigured", "Set Anthropic.ApiKey in Integrations so the AI can read the photos.");
            var photo = await _files.ReadAsync(item.ImagePath, ct);
            if (photo is null) return Fail<Item4DRowDto>("Validation", "The photo file could not be read.");
            var category = await _db.Set<Category>().AsNoTracking().Where(c => c.Id == item.CategoryId).Select(c => c.Name).FirstOrDefaultAsync(ct);

            List<Dish4DPart> parts;
            try { parts = await AskPartsAsync(apiKey, item.Name, category, item.Type, photo.Value.Bytes, photo.Value.MediaType, ct); }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException or HttpRequestException or TaskCanceledException)
            {
                _log.LogWarning(ex, "4D parts for item {Item} failed", itemId);
                return Fail<Item4DRowDto>("AiFailed", "The AI could not read this photo: " + ex.Message);
            }
            if (parts.Count == 0) return Fail<Item4DRowDto>("AiFailed", "The AI found no parts it could label on this photo.");

            var p = await ProfileAsync(itemId, ct);
            p.PartsJson = JsonSerializer.Serialize(parts, Json);
            p.UpdatedOn = DateTime.UtcNow; p.UpdatedBy = Who;
            await _db.SaveChangesAsync(ct);
            return new(true, null, $"Found {parts.Count} parts. Check them, then publish.", await RowAsync(itemId, ct));
        }

        private async Task<List<Dish4DPart>> AskPartsAsync(string apiKey, string name, string? category, string? description, byte[] bytes, string mediaType, CancellationToken ct)
        {
            var model = await _settings.GetRawAsync("Anthropic.Model", ct) ?? "claude-sonnet-4-6";
            const string system =
                "You label the visible parts of a menu photo for a restaurant's interactive 4D menu. " +
                "Return JSON only: {\"parts\":[{\"name\":\"...\",\"detail\":\"...\",\"x\":0,\"y\":0}]}. " +
                "x and y are the centre of that part on the photo, in percent of the width and height (0,0 is the top-left corner); put each point ON the part. " +
                "List 3 to 7 parts a guest can see, the most appetising first (for example bun, patty, melted cheese, fries, sauce, garnish, glass, ice, foam, pack, card art). " +
                "name: 1 to 3 words. detail: one short, appetising sentence that only uses the menu text given or what is clearly visible. " +
                "Never invent allergens, weights, calories, prices or origins. Write in the language of the menu text.";
            var text = $"Item: {name}\nCategory: {category ?? "-"}\nMenu text: {(string.IsNullOrWhiteSpace(description) ? "-" : description)}";
            var content = JsonSerializer.SerializeToElement(new object[]
            {
                new { type = "image", source = new { type = "base64", media_type = mediaType, data = Convert.ToBase64String(bytes) } },
                new { type = "text", text },
            });
            var resp = await _claude.CreateMessageAsync(apiKey, model, system, new[] { new ClaudeMessage { Role = "user", Content = content } }, null, 1500, ct);
            var answer = string.Concat(resp.Content.Where(c => c.Type == "text").Select(c => c.Text ?? ""));
            var s = answer.IndexOf('{'); var e = answer.LastIndexOf('}');
            if (s < 0 || e <= s) throw new JsonException("The answer was not JSON.");
            var arr = JsonNode.Parse(answer[s..(e + 1)])?["parts"] as JsonArray ?? throw new JsonException("No parts in the answer.");
            var parts = arr.OfType<JsonObject>().Select(o => new Dish4DPart(
                o["name"]?.GetValue<string>() ?? "",
                o["detail"]?.GetValue<string>(),
                Num(o["x"]), Num(o["y"]))).ToList();
            return Clean(parts);
        }

        private static double Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : n is JsonValue s && s.TryGetValue<string>(out var str) && double.TryParse(str, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d2) ? d2 : 50;

        public async Task<int> CountUnlabelledAsync(CancellationToken ct = default)
        {
            var labelled = await _db.Set<Item4DProfile>().AsNoTracking().Where(p => p.PartsJson != null && p.PartsJson != "[]").Select(p => p.ItemId).ToListAsync(ct);
            return await PhotoItems().CountAsync(i => !labelled.Contains(i.Id), ct);
        }

        public async Task<int> AnalyzeAllAsync(CancellationToken ct = default)
        {
            var labelled = await _db.Set<Item4DProfile>().AsNoTracking().Where(p => p.PartsJson != null && p.PartsJson != "[]").Select(p => p.ItemId).ToListAsync(ct);
            var ids = await PhotoItems().Where(i => !labelled.Contains(i.Id)).OrderBy(i => i.Id).Select(i => i.Id).ToListAsync(ct);
            var done = 0;
            foreach (var id in ids)
            {
                if (ct.IsCancellationRequested) break;
                var r = await AnalyzeAsync(id, ct);
                if (r.Success) done++;
                else if (r.Error == "NotConfigured") break;
            }
            _log.LogInformation("4D parts: labelled {Done} of {Total} items", done, ids.Count);
            return done;
        }

        public async Task<BaseResponse<Item4DRowDto>> UpdateAsync(int itemId, Item4DUpdateDto dto, CancellationToken ct = default)
        {
            if (!await _db.Set<Item>().AnyAsync(i => i.Id == itemId, ct)) return Fail<Item4DRowDto>("NotFound", $"Item #{itemId} not found.");
            if (dto.RealSizeCm is < 2 or > 120) return Fail<Item4DRowDto>("Validation", "Real size must be between 2 and 120 cm.");
            var parts = Clean(dto.Parts ?? new());
            var p = await ProfileAsync(itemId, ct);
            p.PartsJson = JsonSerializer.Serialize(parts, Json);
            p.PartsPublished = dto.Published && parts.Count > 0;
            p.RealSizeCm = dto.RealSizeCm;
            p.UpdatedOn = DateTime.UtcNow; p.UpdatedBy = Who;
            await _db.SaveChangesAsync(ct);
            return new(true, null, p.PartsPublished ? "Saved and published." : "Saved.", await RowAsync(itemId, ct));
        }

        public async Task<Menu4DBulkResultDto> PublishAllAsync(CancellationToken ct = default)
        {
            var rows = await _db.Set<Item4DProfile>().Where(p => !p.PartsPublished && p.PartsJson != null && p.PartsJson != "[]").ToListAsync(ct);
            var n = 0;
            foreach (var p in rows.Where(r => ParseParts(r.PartsJson).Count > 0)) { p.PartsPublished = true; p.UpdatedOn = DateTime.UtcNow; p.UpdatedBy = Who; n++; }
            await _db.SaveChangesAsync(ct);
            return new Menu4DBulkResultDto(n, n == 0 ? "Nothing new to publish." : $"Published the parts of {n} items.");
        }

        // ── Photo to 3D ──────────────────────────────────────────────────

        public async Task<BaseResponse<Item4DRowDto>> QueueModelAsync(int itemId, CancellationToken ct = default)
        {
            var item = await _db.Set<Item>().AsNoTracking().Where(i => i.Id == itemId).Select(i => new { i.ImagePath }).FirstOrDefaultAsync(ct);
            if (item is null) return Fail<Item4DRowDto>("NotFound", $"Item #{itemId} not found.");
            if (string.IsNullOrWhiteSpace(item.ImagePath)) return Fail<Item4DRowDto>("Validation", "This item has no photo yet.");
            if (string.IsNullOrWhiteSpace(await _settings.GetRawAsync("Menu4D.Meshy.ApiKey", ct)))
                return Fail<Item4DRowDto>("NotConfigured", "Set Menu4D.Meshy.ApiKey in Integrations to turn photos into 3D.");
            var p = await ProfileAsync(itemId, ct);
            if (p.GenStatus is Item4DGenStatus.Queued or Item4DGenStatus.Running) return new(true, null, "Already being made.", await RowAsync(itemId, ct));
            Queue(p);
            await _db.SaveChangesAsync(ct);
            return new(true, null, "Queued. The 3D model is usually ready in a few minutes.", await RowAsync(itemId, ct));
        }

        private void Queue(Item4DProfile p)
        {
            p.GenStatus = Item4DGenStatus.Queued; p.GenProgress = 0; p.GenError = null; p.GenTaskId = null;
            p.GenQueuedOn = DateTime.UtcNow; p.GenStartedOn = null; p.GenFinishedOn = null; p.GenProvider = _to3d.Provider;
            p.UpdatedOn = DateTime.UtcNow; p.UpdatedBy = Who;
        }

        public async Task<Menu4DBulkResultDto> QueueAllModelsAsync(CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(await _settings.GetRawAsync("Menu4D.Meshy.ApiKey", ct)))
                return new Menu4DBulkResultDto(0, "Set Menu4D.Meshy.ApiKey in Integrations to turn photos into 3D.");
            var withModel = await _db.Set<ItemModel3D>().AsNoTracking().Select(m => m.ItemId).ToListAsync(ct);
            var ids = await PhotoItems().Where(i => !withModel.Contains(i.Id)).Select(i => i.Id).ToListAsync(ct);
            var profiles = await _db.Set<Item4DProfile>().Where(p => ids.Contains(p.ItemId)).ToDictionaryAsync(p => p.ItemId, ct);
            var n = 0;
            foreach (var id in ids)
            {
                if (!profiles.TryGetValue(id, out var p)) { p = new Item4DProfile { ItemId = id, UpdatedBy = Who }; _db.Add(p); }
                if (p.GenStatus is Item4DGenStatus.Queued or Item4DGenStatus.Running) continue;
                Queue(p); n++;
            }
            await _db.SaveChangesAsync(ct);
            return new Menu4DBulkResultDto(n, n == 0 ? "Every item with a photo already has a 3D model or is being made."
                : $"Queued {n} items. Estimated cost about ${n * EstimatedCostPerModelUsd:0} with the 3D service; models arrive over the next hours, switched off until you approve them.");
        }

        public async Task<int> ProcessModelQueueAsync(CancellationToken ct = default)
        {
            var apiKey = await _settings.GetRawAsync("Menu4D.Meshy.ApiKey", ct);
            if (string.IsNullOrWhiteSpace(apiKey)) return 0;
            var baseUrl = await _settings.GetRawAsync("Menu4D.Meshy.BaseUrl", ct);
            if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = MeshyClient.DefaultBaseUrl;
            var maxParallel = int.TryParse(await _settings.GetRawAsync("Menu4D.MaxParallel", ct), out var mp) && mp is > 0 and <= 10 ? mp : 3;
            var changed = 0;

            // 1. Collect running jobs
            var running = await _db.Set<Item4DProfile>().Where(p => p.GenStatus == Item4DGenStatus.Running).ToListAsync(ct);
            foreach (var p in running)
            {
                try
                {
                    if (p.GenStartedOn is { } started && DateTime.UtcNow - started > JobTimeout) { Failed(p, "The 3D service took too long. Try again."); changed++; continue; }
                    var t = await _to3d.GetAsync(apiKey, baseUrl, p.GenTaskId!, ct);
                    p.GenProgress = Math.Clamp(t.Progress, 0, 100);
                    if (t.Status == "SUCCEEDED")
                    {
                        if (string.IsNullOrWhiteSpace(t.GlbUrl)) { Failed(p, "The 3D service finished without a model file."); changed++; continue; }
                        var glb = await _to3d.DownloadAsync(t.GlbUrl, ct);
                        byte[]? usdz = null;
                        if (!string.IsNullOrWhiteSpace(t.UsdzUrl)) { try { usdz = await _to3d.DownloadAsync(t.UsdzUrl, ct); } catch (HttpRequestException) { usdz = null; } }
                        var err = await SaveModelAsync(p.ItemId, glb, usdz, ct);
                        if (err is null) { p.GenStatus = Item4DGenStatus.Done; p.GenProgress = 100; p.GenFinishedOn = DateTime.UtcNow; p.GenError = null; }
                        else Failed(p, err);
                        changed++;
                    }
                    else if (t.Status is "FAILED" or "CANCELED" or "EXPIRED") { Failed(p, t.Error ?? "The 3D service could not make a model from this photo."); changed++; }
                }
                catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
                {
                    _log.LogWarning(ex, "4D model job for item {Item} check failed", p.ItemId);
                }
            }
            await _db.SaveChangesAsync(ct);

            // 2. Start queued jobs while there is room
            var slots = maxParallel - await _db.Set<Item4DProfile>().CountAsync(p => p.GenStatus == Item4DGenStatus.Running, ct);
            if (slots <= 0) return changed;
            var queued = await _db.Set<Item4DProfile>().Where(p => p.GenStatus == Item4DGenStatus.Queued).OrderBy(p => p.GenQueuedOn).Take(slots).ToListAsync(ct);
            foreach (var p in queued)
            {
                var path = await _db.Set<Item>().AsNoTracking().Where(i => i.Id == p.ItemId).Select(i => i.ImagePath).FirstOrDefaultAsync(ct);
                var photo = string.IsNullOrWhiteSpace(path) ? null : await _files.ReadAsync(path, ct);
                if (photo is null) { Failed(p, "The photo file could not be read."); changed++; continue; }
                try
                {
                    var dataUri = $"data:{photo.Value.MediaType};base64,{Convert.ToBase64String(photo.Value.Bytes)}";
                    p.GenTaskId = await _to3d.CreateAsync(apiKey, baseUrl, dataUri, ct);
                    p.GenStatus = Item4DGenStatus.Running; p.GenStartedOn = DateTime.UtcNow; p.GenProgress = 0; p.GenProvider = _to3d.Provider;
                    changed++;
                }
                catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
                {
                    Failed(p, ex.Message); changed++;
                }
            }
            await _db.SaveChangesAsync(ct);
            return changed;
        }

        private static void Failed(Item4DProfile p, string message)
        {
            p.GenStatus = Item4DGenStatus.Failed; p.GenError = message.Length > 1000 ? message[..1000] : message; p.GenFinishedOn = DateTime.UtcNow;
        }

        /// <summary>Stores the AI model as the item's 3D model, switched off until staff approve it.</summary>
        private async Task<string?> SaveModelAsync(int itemId, byte[] glb, byte[]? usdz, CancellationToken ct)
        {
            if (glb.Length < 4 || glb[0] != (byte)'g' || glb[1] != (byte)'l' || glb[2] != (byte)'T' || glb[3] != (byte)'F') return "The 3D service returned a file that is not a GLB model.";
            if (usdz is { Length: > 4 } && !(usdz[0] == 0x50 && usdz[1] == 0x4B)) usdz = null;
            string? glbPath = null, usdzPath = null;
            try
            {
                glbPath = await _media.SaveAsync(new BytesFormFile(glb, "model.glb", "model/gltf-binary"), "models", MediaKind.Model3D, ct);
                if (usdz is not null) usdzPath = await _media.SaveAsync(new BytesFormFile(usdz, "model.usdz", "model/vnd.usdz+zip"), "models", MediaKind.Model3D, ct);
            }
            catch (ArgumentException ex) { await _media.DeleteAsync(glbPath); return ex.Message; }

            var row = await _db.Set<ItemModel3D>().FirstOrDefaultAsync(m => m.ItemId == itemId, ct);
            var old = new List<string?>();
            if (row is null)
            {
                _db.Add(new ItemModel3D { ItemId = itemId, GlbPath = glbPath, GlbBytes = glb.Length, UsdzPath = usdzPath, IsActive = false, CreatedOn = DateTime.UtcNow, UpdatedBy = "AI 3D" });
            }
            else
            {
                old.Add(row.GlbPath); old.Add(row.UsdzPath);
                row.GlbPath = glbPath; row.GlbBytes = glb.Length; row.UsdzPath = usdzPath; row.IsActive = false; row.UpdatedOn = DateTime.UtcNow; row.UpdatedBy = "AI 3D";
            }
            await _db.SaveChangesAsync(ct);
            foreach (var o in old) await _media.DeleteAsync(o);
            return null;
        }
    }

    /// <summary>In-memory file handed to the media storage (which expects an upload).</summary>
    public sealed class BytesFormFile : IFormFile
    {
        private readonly byte[] _bytes;
        public BytesFormFile(byte[] bytes, string fileName, string contentType) { _bytes = bytes; FileName = fileName; Name = "file"; ContentType = contentType; }
        public string ContentType { get; }
        public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{FileName}\"";
        public IHeaderDictionary Headers { get; } = new EmptyHeaders();
        public long Length => _bytes.Length;
        public string Name { get; }
        public string FileName { get; }
        public Stream OpenReadStream() => new MemoryStream(_bytes, writable: false);
        public void CopyTo(Stream target) => target.Write(_bytes, 0, _bytes.Length);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default) => target.WriteAsync(_bytes, 0, _bytes.Length, cancellationToken);

        private sealed class EmptyHeaders : Dictionary<string, Microsoft.Extensions.Primitives.StringValues>, IHeaderDictionary
        {
            public long? ContentLength { get; set; }
        }
    }

    /// <summary>Hangfire entry points.</summary>
    public class Menu4DJobs
    {
        private readonly IMenu4DService _svc;
        public Menu4DJobs(IMenu4DService svc) { _svc = svc; }
        public Task<int> ProcessModels() => _svc.ProcessModelQueueAsync();
        public Task<int> LabelAll() => _svc.AnalyzeAllAsync();
    }
}
